/*

 - use distance concept?

*/
#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>		// also defines min/max!
#include <math.h>
#include <time.h>
#include <string.h>

#if defined(_MSC_VER)
#define NDEBUG			// in order to disable assert, if not set the assert are compiled in Release mode
#endif
#include <assert.h>

#define THREADS 1		// number of threads
#define SCAN_LAST_TO_FIRST 1
#define RANDOMIZE_ORDER 0
#ifndef NN
#define NN 9
#endif
#ifndef SCORING_WITH_AREA
#define SCORING_WITH_AREA 0
#endif
#define NO_RANDOM_IN_SCORING 0
#ifndef USE_RANDOMIZED_SCORING
#define USE_RANDOMIZED_SCORING 0
#endif

#ifndef USE_PATTERN
#define USE_PATTERN 1
#endif
#define FAST_PATTERN 1

#define USE_HEURISTIC 0
#define OPTIMIZER 1

//#if OPTIMIZER
//#define GRID_SIZE 128
//
//#else
#if NN <= 12
#define GRID_SIZE 64	// for N upto 12?
#else
#define GRID_SIZE 128
#endif
//#endif

#if NN <= 8
#define UPPERB 85
#elif NN==9
#define UPPERB 95
#elif NN==10
#define UPPERB 104
#elif NN==11
#define UPPERB 114
#elif NN==12
#define UPPERB 123
#elif NN==13
#define UPPERB 131
#elif NN==14
#define UPPERB 143
#elif NN==15
#define UPPERB 150
#elif NN==16
#define UPPERB 159
#elif NN==17
#define UPPERB 167
#elif NN==18
#define UPPERB 178
#elif NN==19
#define UPPERB 183
#elif NN==20
#define UPPERB 190
#elif NN==21
#define UPPERB 199
#elif NN==22
#define UPPERB 204
#elif NN==23
#define UPPERB 216
#elif NN==24
#define UPPERB 225
#elif NN==25
#define UPPERB 228
#elif NN==26
#define UPPERB 237
#elif NN==27
#define UPPERB 243
#elif NN==28
#define UPPERB 254
#elif NN==29
#define UPPERB 263
#elif NN==30
#define UPPERB 269
#elif NN==31
#define UPPERB 276
#else
#define UPPERB 276		//upper bound on solution
#endif

#define MASK unsigned int
#define MASK_SIZE (sizeof(MASK)*8)
#define ROUND_MASK(x) (((x) + (MASK_SIZE-1))/MASK_SIZE)
#define SET_BIT(M,B) (M[(B)/MASK_SIZE] |= (1U<<((B)%MASK_SIZE)))
//#define XOR_BIT(M,B) (M[(B)/MASK_SIZE] ^= (1U<<((B)%MASK_SIZE)))
//#define CLEAR_BIT(M,B) (M[(B)/MASK_SIZE] &= ~(1U<<((B)%MASK_SIZE)))
#define IS_BIT_SET(M,B)  (M[(B)/MASK_SIZE] & (1U<<((B)%MASK_SIZE)))

#define MAXFRONT (4*UPPERB)
#define AREA (GRID_SIZE * GRID_SIZE)
#define CENTER ((GRID_SIZE / 2) * GRID_SIZE+ GRID_SIZE/ 2)


typedef struct
{
	float score;
	unsigned int hash;
	unsigned short numberOfStones;
	unsigned short ones[NN];
	unsigned short stones[UPPERB];

	MASK isFrontier[ROUND_MASK(AREA)];
	MASK isFilled[ROUND_MASK(AREA)];
#if USE_PATTERN
	MASK isOne[ROUND_MASK(AREA)];		// when set, we cannot place a one here
#endif
	short csum[AREA];
	unsigned short frontier[MAXFRONT];		// adjacent empty cells
	unsigned short nbFrontiers;			// history upper index of frontier cells

	unsigned char numberOfOnes;
	unsigned char xmin, xmax, ymin, ymax;
#if THREADS > 1
	//volatile int locks;		// for debugging purposes
	volatile char lock;
#endif
} Board;

static unsigned int HashPrime;
static Board* read, * write;
static volatile int countAddedOnes;

typedef struct
{
	// we use such a structure to improve the CPU cache
	Board b2;
} Work;
#if THREADS > 1
Work WorkUnit[THREADS];
static volatile char lockSave;
#else
Work WorkUnit;
#endif

static void PlayAllMoves(Board* b, int currentStone, Board* b2);

#ifdef _MSC_VER
#include "Beam_windows_specific.h"
#else
#include "Beam_linux_specific.h"
#endif
#if USE_PATTERN
#include "pattern.h"
#endif

#define INFINITERUN 1

#include "records.h"

static int minimalX, minimalY, maximalX, maximalY, maxFrontiers;
static int totalMinimalX, totalMinimalY, totalMaximalX, totalMaximalY, totalMaxFrontiers;
static int biggestArea;

#define direction0 (-GRID_SIZE - 1)
#define direction1 (-GRID_SIZE)
#define direction2 (-GRID_SIZE + 1)
#define direction3 (-1)
#define direction4 (1)
#define direction5 (GRID_SIZE - 1)
#define direction6 (GRID_SIZE)
#define direction7 (GRID_SIZE + 1)

static float Coef1, Coef2, Coef3, Coef4;
static int iteration = 0;

static time_t startTime;
static unsigned int ZobristPosition[UPPERB][AREA];
struct
{
	unsigned char x, y;
} offsets[AREA];

static Board* grids1, * grids2;

static float GET_FLOATRAND(void)
{
	return GET_RAND32() / 4294967296.0;
}

static int isPrime(unsigned int n)
{
	if (!(n & 1)) return 0;
	int s = (int)sqrt((float)n);
	for (int i = 3; i <= s; i += 2)
	{
		if ((n % i) == 0)
		{
			return 0;
		}
	}
	return 1;
}

static char SaveLogFilename[256];

static char* GetSaveLog()
{
	return "logs.txt";
	sprintf(SaveLogFilename, "logs%d.txt", HashPrime);
	return SaveLogFilename;
}

static unsigned int NextLowestPrime(unsigned int n)
{
	do
	{
		--n;
	} while (!isPrime(n));
	return n;
}

static void initZobrist(void)
{
	for (int y = 0; y < UPPERB; ++y)
	{
		for (int x = 0; x < AREA; ++x)
		{
			ZobristPosition[y][x] = GET_RAND32();
		}
	}
}

static void SaveGrid(const Board* b)
{
	char saveFilename[256];
	unsigned short cells[AREA];
	sprintf(saveFilename, "output%02d-%03d.txt", b->numberOfOnes, b->numberOfStones + 1);
	FILE* out = fopen(saveFilename, "at");

	memset(cells, 0, sizeof(cells));
	for (int i = 0; i < b->numberOfOnes; ++i)
		cells[b->ones[i]] = 1;
	for (int i = 0; i < b->numberOfStones; ++i)
		cells[b->stones[i]] = i + 2;

	for (int y = b->ymin; y <= b->ymax; ++y)
	{
		for (int x = b->xmin; x <= b->xmax; ++x)
		{
			fprintf(out, "%03d ", cells[y * GRID_SIZE + x]);
		}
		fprintf(out, "\n");
	}
	fprintf(out, "%d,%d %f %f %f %f\n", b->numberOfOnes, b->numberOfStones + 1, Coef1, Coef2, Coef3, Coef4);
	//new Al's format:
	for (int y = b->ymin; y <= b->ymax; ++y)
	{
		int spaces = 0;
		int comma = 0;
		if (y > b->ymin)
			fprintf(out, ",");

		fprintf(out, "(");
		for (int x = b->xmin; x <= b->xmax; ++x)
		{
			int cell = cells[y * GRID_SIZE + x];
			if (cell == 0)
			{
				++spaces;
				continue;
			}
			if (comma)
				fprintf(out, ",");
			if (spaces)
			{
				fprintf(out, "-%d,", spaces);
				spaces = 0;
			}
			fprintf(out, "%d", cell);
			comma = 1;
		}
		fprintf(out, ")");
	}
	fprintf(out, "\n");
	fclose(out);
}

static void DumpGrid(const Board* b)
{
	unsigned short cells[AREA];
	memset(cells, 0, sizeof(cells));
	for (int i = 0; i < b->numberOfOnes; ++i)
		cells[b->ones[i]] = 1;
	for (int i = 0; i < b->numberOfStones; ++i)
		cells[b->stones[i]] = i + 2;

	for (int y = b->ymin; y <= b->ymax; ++y)
	{
		for (int x = b->xmin; x <= b->xmax; ++x)
		{
			int o = y * GRID_SIZE + x;
			printf("%03d ", cells[o]);
		}
		printf("\n");
	}
	printf("\n");
	__debugbreak();
}

static void InitBoard(Board* b)
{
	memset(b, 0, sizeof(Board));
	b->xmin = b->ymin = GRID_SIZE;

	for (int i = 0; i < GRID_SIZE; ++i)
	{  //block off outside edge
		int o = i * GRID_SIZE;
		assert(o >= 0 && o < AREA);
		SET_BIT(b->isFilled, o);
		SET_BIT(b->isFrontier, o);
		o = i * GRID_SIZE + GRID_SIZE - 1;
		assert(o >= 0 && o < AREA);
		SET_BIT(b->isFilled, o);
		SET_BIT(b->isFrontier, o);
		assert(o >= 0 && o < AREA);
		SET_BIT(b->isFilled, o);
		SET_BIT(b->isFrontier, i);
		o = i + (GRID_SIZE - 1) * GRID_SIZE;
		assert(o >= 0 && o < AREA);
		SET_BIT(b->isFilled, o);
		SET_BIT(b->isFrontier, o);
	}
}

#define makeMoveMacroPtrWithoutFrontier(workboard, source, dir, content) {\
	int p3 =  source + dir;\
	assert(p3 >= 0 && p3 < AREA);\
	workboard->csum[p3] += content;\
}

#define makeMoveMacroPtrWithFrontier(workboard, source, dir, content) {\
	int p3 =  source + dir;\
	assert(p3 >= 0 && p3 < AREA);\
	workboard->csum[p3] += content;\
	if (!IS_BIT_SET(workboard->isFrontier, p3))\
	{\
		assert(!IS_BIT_SET(workboard->isFilled, p3));\
		workboard->frontier[workboard->nbFrontiers++] = p3;\
		SET_BIT(workboard->isFrontier, p3);\
	}\
}

// with frontiers
static void PlayMove(Board* b, int p1, int current)
{
	assert(p1 >= 0 && p1 < AREA);
	assert(!IS_BIT_SET(b->isFilled, p1));
	assert(b->numberOfStones + 2 == current);
	assert(b->numberOfStones < UPPERB);
	assert(current >= 1 && current < UPPERB);
	assert(b->csum[p1] == current);
	b->stones[b->numberOfStones] = p1;
	++b->numberOfStones;
	SET_BIT(b->isFilled, p1);
	SET_BIT(b->isFrontier, p1);
	b->hash ^= ZobristPosition[current][p1];
	b->xmax = max(b->xmax, offsets[p1].x);
	b->xmin = min(b->xmin, offsets[p1].x);
	b->ymax = max(b->ymax, offsets[p1].y);
	b->ymin = min(b->ymin, offsets[p1].y);

	makeMoveMacroPtrWithFrontier(b, p1, direction0, current);
	makeMoveMacroPtrWithFrontier(b, p1, direction1, current);
	makeMoveMacroPtrWithFrontier(b, p1, direction2, current);
	makeMoveMacroPtrWithFrontier(b, p1, direction3, current);
	makeMoveMacroPtrWithFrontier(b, p1, direction4, current);
	makeMoveMacroPtrWithFrontier(b, p1, direction5, current);
	makeMoveMacroPtrWithFrontier(b, p1, direction6, current);
	makeMoveMacroPtrWithFrontier(b, p1, direction7, current);
	maxFrontiers = max(maxFrontiers, b->nbFrontiers);	// for optimization purposes
}

#if USE_PATTERN
static void PreparePattern()
{
	// optimization: we simply multiply all the Y coordinates by the GRID_SIZE, to save one multiply (or shift) for each Y
	for (int bb = 0; bb < sizeof(pattern) / sizeof(int); bb += 2)
	{
		pattern[bb + 1] *= GRID_SIZE;
	}
}
#endif

#if !USE_PATTERN
// no pattern -> always valid!
static int CheckPattern(Board* b, int p1)
{
	return 1;
}
#elif !FAST_PATTERN
// slow pattern checker (accurate)
static int CheckPattern(Board* b, int p1)
{
	// ban the cells for ones
	int x1 = offsets[p1].x;
	int y1 = offsets[p1].y * GRID_SIZE;

	for (int bb = 0; bb < sizeof(pattern) / sizeof(int); bb += 2)
	{
		int x, y;
		int dx = pattern[bb];
		int dy = pattern[bb + 1];
		x = x1 + dx;
		y = y1 + dy;
		if (x >= 0 && x < GRID_SIZE && y >= 0 && y < AREA)
		{
			if (IS_BIT_SET(b->isOne, y + x))
				return 0;
		}

		if (dx != 0)
		{
			x = x1 - dx;
			y = y1 + dy;
			if (x >= 0 && x < GRID_SIZE && y >= 0 && y < AREA)
			{
				if (IS_BIT_SET(b->isOne, y + x))
					return 0;
			}
		}

		if (dy != 0)
		{
			x = x1 + dx;
			y = y1 - dy;
			if (x >= 0 && x < GRID_SIZE && y >= 0 && y < AREA)
			{
				if (IS_BIT_SET(b->isOne, y + x))
					return 0;
			}

			x = x1 - dx;
			y = y1 - dy;
			if (x >= 0 && x < GRID_SIZE && y >= 0 && y < AREA)
			{
				if (IS_BIT_SET(b->isOne, y + x))
					return 0;
			}
		}
	}
	return 1;
}
#else
// fast pattern checker, the pattern checking might "spill" horizontally
static int CheckPattern(Board* b, int p1)
{
	// ban the cells for ones
	for (int bb = 0; bb < sizeof(pattern) / sizeof(int); bb += 2)
	{
		int dx = pattern[bb];
		int dy = pattern[bb + 1];

		int o1 = dx + dy + p1;
		if (o1 >= 0 && o1 < AREA && IS_BIT_SET(b->isOne, o1))
		{
			return 0;
		}

		if (dx != 0)
		{
			o1 = -dx + dy + p1;
			if (o1 >= 0 && o1 < AREA && IS_BIT_SET(b->isOne, o1))
			{
				return 0;
			}
		}

		if (dy != 0)
		{
			o1 = dx - dy + p1;
			if (o1 >= 0 && o1 < AREA && IS_BIT_SET(b->isOne, o1))
			{
				return 0;
			}
			o1 = -dx - dy + p1;
			if (o1 >= 0 && o1 < AREA && IS_BIT_SET(b->isOne, o1))
			{
				return 0;
			}
		}
	}
	return 1;
}
#endif

// without frontiers
static void PlayOne(Board* b, int p1)
{
	assert(b->numberOfOnes < NN);
	b->ones[b->numberOfOnes] = p1;
	++b->numberOfOnes;

	assert(p1 >= 0 && p1 < AREA);
	assert(!IS_BIT_SET(b->isFilled, p1));
	SET_BIT(b->isFilled, p1);
	SET_BIT(b->isFrontier, p1);
#if USE_PATTERN
	SET_BIT(b->isOne, p1);
#endif
	int current = 1;
	b->hash ^= ZobristPosition[current][p1];
	b->xmax = max(b->xmax, offsets[p1].x);
	b->xmin = min(b->xmin, offsets[p1].x);
	b->ymax = max(b->ymax, offsets[p1].y);
	b->ymin = min(b->ymin, offsets[p1].y);

	makeMoveMacroPtrWithoutFrontier(b, p1, direction0, current);
	makeMoveMacroPtrWithoutFrontier(b, p1, direction1, current);
	makeMoveMacroPtrWithoutFrontier(b, p1, direction2, current);
	makeMoveMacroPtrWithoutFrontier(b, p1, direction3, current);
	makeMoveMacroPtrWithoutFrontier(b, p1, direction4, current);
	makeMoveMacroPtrWithoutFrontier(b, p1, direction5, current);
	makeMoveMacroPtrWithoutFrontier(b, p1, direction6, current);
	makeMoveMacroPtrWithoutFrontier(b, p1, direction7, current);
}

static void StoreGrid(Board* grid, int lastStone)
{
#if THREADS > 1
	assert(!grid->lock);
#endif
#ifdef _DEBUG
	unsigned int hh = 0;
	assert(lastStone == grid->numberOfStones + 1);
	for (int i = 0; i < grid->numberOfStones; ++i)
	{
		int v = grid->stones[i];
		assert(v < AREA);
		assert(i + 2 < UPPERB);
		hh ^= ZobristPosition[i + 2][v];
	}
	for (int i = 0; i < grid->numberOfOnes; ++i)
	{
		int v = grid->ones[i];
		assert(v < AREA);
		hh ^= ZobristPosition[1][v];
	}
	assert(hh == grid->hash);
#endif

#if USE_HEURISTIC
	// heuristic
	if (grid->numberOfOnes >= 4 && lastStone < records[grid->numberOfOnes - 2])
		return;
#endif

	int nbDistinctSums = 0;
	unsigned int distinctSumMask[ROUND_MASK(UPPERB)];
	memset(distinctSumMask, 0, sizeof(distinctSumMask));
	int countSmallSums = 0;
	int countLargeSums = 0;
	int maximalSum = UPPERB - lastStone - 1;
	for (int f = 0; f < grid->nbFrontiers; ++f)
	{
		int p1 = grid->frontier[f];
		assert(p1 >= 0 && p1 < AREA);
		if (!IS_BIT_SET(grid->isFilled, p1))
		{
			int s = grid->csum[p1];
			if (s < UPPERB)
			{
				if (s > lastStone)
				{
					if (!IS_BIT_SET(distinctSumMask, s))
					{
						SET_BIT(distinctSumMask, s);
						++nbDistinctSums;
					}
					++countLargeSums;
				}
				else if (grid->numberOfOnes < NN && s == lastStone)
				{
					++countLargeSums;
				}
				else if (s < maximalSum)
				{
					++countSmallSums;
				}
			}
		}
	}

#if USE_RANDOMIZED_SCORING
	int area = (grid->xmax - grid->xmin + 1) * (grid->xmax - grid->xmin + 1) + (grid->ymax - grid->ymin + 1) * (grid->ymax - grid->ymin + 1);
	float coefArea = (float)area / (float)biggestArea;
	float score = (NN - grid->numberOfOnes) * 1E6 + nbDistinctSums * Coef1 + countLargeSums * Coef2 + countSmallSums * Coef3 + coefArea * Coef4 + GET_FLOATRAND();
#elif SCORING_WITH_AREA
	int area = (grid->xmax - grid->xmin + 1) * (grid->xmax - grid->xmin + 1) + (grid->ymax - grid->ymin + 1) * (grid->ymax - grid->ymin + 1);
	float coefArea = (float)area / (float)biggestArea;
	float score = (((NN - grid->numberOfOnes) * 512 + nbDistinctSums + coefArea) * 512 + countLargeSums) * 512 + countSmallSums + GET_FLOATRAND();
#elif NO_RANDOM_IN_SCORING
	float score = (((NN - grid->numberOfOnes) * 512 + nbDistinctSums) * 512 + countLargeSums) * 512 + countSmallSums;
#else
	float score = (((NN - grid->numberOfOnes) * 512 + nbDistinctSums) * 512 + countLargeSums) * 512 + countSmallSums + GET_FLOATRAND();
#endif

	unsigned int h = grid->hash % HashPrime;
#if THREADS > 1
	CreateLock(&write[h].lock);
#endif
	if (!write[h].numberOfStones || score > write[h].score)
	{
//#if THREADS > 1
//		grid->locks = write[h].locks + locks;
//#endif
		grid->score = score;
		memcpy(&write[h], grid, sizeof(Board));
//		//write[h] = *grid;
//#ifndef _MSC_VER
//		// avoid memory corruption?
//		//_mm_mfence();
//#endif
	}
#if THREADS > 1
	else
		ReleaseLock(&write[h].lock);
#endif
}

// new detection of placing ones!!!
#define getMovesMacro(dir) {\
	int p2 = p1 + dir;\
	assert(p2 >= 0 && p2 < AREA);\
	if (!IS_BIT_SET(b->isFrontier, p2) && CheckPattern(b, p2))\
	{\
		assert(!IS_BIT_SET(b->isFilled, p2)); \
		*b2 = *b; \
		++countAddedOnes; \
		PlayOne(b2, p2);\
		PlayMove(b2, p1, currentStone);\
		/*DumpGrid(b2);*/\
		StoreGrid(b2, currentStone);\
	}\
}

// play all the moves with the stone "currentLevel"
static void PlayAllMoves(Board* b, int currentStone, Board* b2)
{
	assert(b >= read && b < &read[HashPrime]);
	assert(b->numberOfStones + 2 == currentStone);
	// decompress here
	// 2 always at the center !
	assert(b->stones[0] == CENTER);
	assert(b->numberOfOnes <= NN);

	assert(b->nbFrontiers < MAXFRONT);
#if RANDOMIZE_ORDER
	for (int f = b->nbFrontiers; f != 0; --f)
	{
		int r = GET_RAND32() % f;
		// we swap the order
		int p1 = b->frontier[r];
		b->frontier[r] = b->frontier[f - 1];
		b->frontier[f - 1] = p1;
		assert(p1 >= 0 && p1 < AREA);
		if (!IS_BIT_SET(b->isFilled, p1))
		{
			if (b->csum[p1] == currentStone)
			{
				//add number alone
				*b2 = *b;
				PlayMove(b2, p1, currentStone);
				StoreGrid(b2, currentStone);
			}
			else if (b->csum[p1] == currentStone - 1 && b->numberOfOnes < NN)
			{
				//add number and a 1
				getMovesMacro(direction0);
				getMovesMacro(direction1);
				getMovesMacro(direction2);
				getMovesMacro(direction3);
				getMovesMacro(direction4);
				getMovesMacro(direction5);
				getMovesMacro(direction6);
				getMovesMacro(direction7);
			}
		}
}
#else
#if SCAN_LAST_TO_FIRST
	for (int f = b->nbFrontiers - 1; f >= 0; --f)
#else
	for (int f = 0; f < b->nbFrontiers; ++f)
#endif
	{
		int p1 = b->frontier[f];
		assert(p1 >= 0 && p1 < AREA);
		if (!IS_BIT_SET(b->isFilled, p1))
		{
			if (b->csum[p1] == currentStone)
			{
				//add number alone
				*b2 = *b;
				PlayMove(b2, p1, currentStone);
				StoreGrid(b2, currentStone);
			}
			else if (b->csum[p1] == currentStone - 1 && b->numberOfOnes < NN)
			{
				//add number and a 1
				getMovesMacro(direction0);
				getMovesMacro(direction1);
				getMovesMacro(direction2);
				getMovesMacro(direction3);
				getMovesMacro(direction4);
				getMovesMacro(direction5);
				getMovesMacro(direction6);
				getMovesMacro(direction7);
			}
		}
	}
#endif
}

static int compare(const void* a, const void* b)
{
	const Board* n1 = (Board*)a;
	const Board* n2 = (Board*)b;
	if (n1->score < n2->score) return 1;
	if (n1->score > n2->score) return -1;
	return 0;
}

static int Compute(int currentStone)
{
	Board* tmp = read;
	read = write;
	write = tmp;

	qsort(read, HashPrime, sizeof(Board), compare);
	memset(write, 0, HashPrime * sizeof(Board));
	countAddedOnes = 0;

#if THREADS <= 1
	for (unsigned int i = 0; i < HashPrime; ++i)
	{
		Board* g = &read[i];
		if (g->numberOfStones)
		{
			assert(g->numberOfStones + 2 == currentStone);
			PlayAllMoves(g, currentStone, &WorkUnit.b2);
		}
	}
#else
	DistributeComputation();
#endif

	int minOnes = 1 << 20;
	int maxOnes = -(1 << 20);

	int countStats[NN + 1] = { 0 };
	//for (int i = 0; i <= NN; ++i)
	//	countStats[i] = 0;
	int countGrids = 0;
	//int countRecords = 0;
	for (unsigned int i = 0; i < HashPrime; ++i)
	{
		const Board* b = &write[i];
		if (b->numberOfStones)
		{
			assert(b->numberOfStones + 1 == currentStone);

			minimalX = min(minimalX, b->xmin);
			maximalX = max(maximalX, b->xmax);
			minimalY = min(minimalY, b->ymin);
			maximalY = max(maximalY, b->ymax);

			int a = (b->xmax - b->xmin + 1) * (b->xmax - b->xmin + 1) + (b->ymax - b->ymin + 1) * (b->ymax - b->ymin + 1);
			biggestArea = max(biggestArea, a);

			++countGrids;
			++countStats[b->numberOfOnes];
			minOnes = min(minOnes, b->numberOfOnes);
			maxOnes = max(maxOnes, b->numberOfOnes);
			if (currentStone >= records[b->numberOfOnes] && b->numberOfOnes >= 6)
			{
				records[b->numberOfOnes] = currentStone;
				SaveGrid(b);
			}
		}
	}

	totalMinimalX = min(totalMinimalX, minimalX);
	totalMaximalX = max(totalMaximalX, maximalX);
	totalMinimalY = min(totalMinimalY, minimalY);
	totalMaximalY = max(totalMaximalY, maximalY);
	totalMaxFrontiers = max(totalMaxFrontiers, maxFrontiers);
	assert(maxFrontiers < MAXFRONT);

	time_t curTime;
	time(&curTime);
	printf("%d: %d %d %d/%d %ds", currentStone, minOnes, maxOnes, countGrids, HashPrime, (int)(curTime - startTime));
	for (int i = minOnes; i <= maxOnes; ++i)
	{
		printf(" %d(%d)", countStats[i], i);
	}
	printf("\n");
	FILE* oo = fopen(GetSaveLog(), "a");
	fprintf(oo, "%d: %d %d %d/%d %ds", currentStone, minOnes, maxOnes, countGrids, HashPrime, (int)(curTime - startTime));
	for (int i = minOnes; i <= maxOnes; ++i)
	{
		fprintf(oo, " %d(%d)", countStats[i], i);
	}
	fprintf(oo, " X=%d-%d Y=%d-%d F=%d/%d A=%d O=%d\n", minimalX, maximalX, minimalY, maximalY, maxFrontiers, MAXFRONT, biggestArea, countAddedOnes);
	fclose(oo);
	return countGrids;
	}

#define CopyOne(dir) if(src+dir>=0 && src+dir < AREA){ assert(pos+dir>=0 && pos+dir < AREA); if (!IS_BIT_SET(b.isFilled, pos + dir) && values[src + dir] == 1) {PlayOne(&b, pos + dir);}}

static int StoreSolution(int startLevel, int maxval, const int values[AREA], const int playposx[UPPERB], const int playposy[UPPERB])
{
	int bad = 0;

	if (maxval < startLevel || maxval < 2)
	{
		bad = 1;
		printf("Corrupted 0: %d <> %d grid\n", maxval, startLevel);
	}
	else
	{
		Board b;
		InitBoard(&b);

		int startx = GRID_SIZE / 2 - playposx[2];
		int starty = GRID_SIZE / 2 - playposy[2];

		for (int move = 2; move <= startLevel; ++move)
		{
			int newX = playposx[move] + startx;
			int newY = playposy[move] + starty;
			assert(newX >= 0 && newX < GRID_SIZE);
			assert(newY >= 0 && newY < GRID_SIZE);
			int pos = newY * GRID_SIZE + newX;
			assert(pos >= 0 && pos < AREA);
			if (b.csum[pos] == move)
			{
				PlayMove(&b, pos, move);
			}
			else
			{
				int src = playposy[move] * GRID_SIZE + playposx[move];
				assert(src >= 0 && src < AREA);
				// we place the ones
				if (b.csum[pos] > move)
				{
					bad = 1;
					printf("Corrupted 1: %d <> %d grid\n", move, b.csum[pos]);
					break;
				}
				CopyOne(direction0);
				CopyOne(direction1);
				CopyOne(direction2);
				CopyOne(direction3);
				CopyOne(direction4);
				CopyOne(direction5);
				CopyOne(direction6);
				CopyOne(direction7);
				assert(b.csum[pos] == move);
				PlayMove(&b, pos, move);
			}
		}
		// 2 always at the center!
		assert(b.stones[0] == CENTER);
		if (!bad)
			StoreGrid(&b, startLevel);
	}
	return bad;
}

static int Load(const char* filename, int originalStart, int* originalLevel)
{
	int x, y;
	FILE* fp;
	int values[AREA];
	int playposx[UPPERB], playposy[UPPERB];
	int maxval = 0;
	int xmax = 0, ymax = 0;
	printf("Load %s\n", filename);
	fp = fopen(filename, "r");
	if (fp == NULL)
	{
		printf("Can't open file\n");
		return 0;
	}
	int startLevel = originalStart;
	int countSolutions = 0;
	y = 0;
	memset(values, 0, sizeof(values));
	while (!feof(fp))
	{
		char line[1024] = { 0 };
		const char* p = line;
		fgets(line, 1023, fp);
		int solution = 1;
		if (memcmp(line, "Solution", 8) == 0)
			solution = 0;
		else
		{
			while (*p)
			{
				if (*p == ',' || *p == '(')
				{
					solution = 0;
					break;
				}
				++p;
			}
		}

		if (!solution)
		{
			if (y != 0)
			{
				++countSolutions;
				if (startLevel < 0)
				{
					startLevel = startLevel + maxval;
				}
				if (!StoreSolution(startLevel, maxval, values, playposx, playposy))
					*originalLevel = maxval;
				memset(values, 0, sizeof(values));
			}

			maxval = 0;
			xmax = ymax = 0;
			y = 0;
		}
		else
		{

			p = line;

			x = 0;
			for (;;)
			{
				int val = 0;
				while (*p >= '0' && *p <= '9')
				{
					val = val * 10 + *p - '0';
					++p;
				}

				if (val >= UPPERB)
				{
					printf("Error %d/%d\n", val, UPPERB);
					exit(0);
				}

				if (x >= GRID_SIZE || y >= GRID_SIZE)
				{
					printf("Out of range\n");
					exit(0);
				}
				values[y * GRID_SIZE + x] = val;
				if (val > 1)
				{
					playposx[val] = x;
					playposy[val] = y;
				}
				maxval = max(maxval, val);
				xmax = max(xmax, x);
				ymax = max(ymax, y);
				++x;
				if (*p != ' ')
					break;
				++p;
			}
			++y;
		}
	}
	if (y != 0)
	{
		++countSolutions;
		if (startLevel < 0)
		{
			startLevel = startLevel + maxval;
		}
		if (!StoreSolution(startLevel, maxval, values, playposx, playposy))
			*originalLevel = maxval;
	}
	fclose(fp);
	printf("%d solutions at %d\n", countSolutions, startLevel);
	return startLevel;
}

static int PerformComputation(char* filename, int start, int end, int startingPosition)
{
	maxFrontiers = 0;
	minimalX = minimalY = GRID_SIZE;
	maximalX = maximalY = 0;
	biggestArea = 1;

	memset(grids1, 0, HashPrime * sizeof(Board));
	memset(grids2, 0, HashPrime * sizeof(Board));

	read = grids1;
	write = grids2;

	int currentLevel;
	printf("HashPrime=%d\n", HashPrime);
	if (filename != NULL)
	{
		int originalLevel;
		printf("Load %s\n", filename);
		currentLevel = Load(filename, start, &originalLevel);
		if (currentLevel == 0)
			return 0;
		FILE* oo = fopen(GetSaveLog(), "a");
		fprintf(oo, "Loading %s\n", filename);
		fclose(oo);
		//if (end != 0)
		//	end = originalLevel + end;
	}
	else
	{
		if (startingPosition == 6)
		{
			Board b;
			InitBoard(&b);

			b.xmax = b.xmin = offsets[CENTER].x;
			b.ymax = b.ymin = offsets[CENTER].y;
			PlayOne(&b, CENTER + direction0);
			PlayOne(&b, CENTER + direction7);
			PlayMove(&b, CENTER, 2);
			PlayMove(&b, CENTER - 1, 3);
			StoreGrid(&b, 3);
			currentLevel = 3;
		}
		else if (startingPosition >= 6)
		{
			Board b;
			InitBoard(&b);

			b.xmax = b.xmin = offsets[CENTER].x;
			b.ymax = b.ymin = offsets[CENTER].y;
			PlayOne(&b, CENTER + direction1);
			PlayOne(&b, CENTER + direction3);
			PlayMove(&b, CENTER, 2);
			PlayMove(&b, CENTER + direction2, 3);
			StoreGrid(&b, 3);
			currentLevel = 3;
		}
		else
		{
			const int sp1[6] = { direction0, direction0, direction0, direction0, direction1, direction3 };
			const int sp2[6] = { direction1, direction2, direction4, direction7, direction3, direction4 };
			// start from scratch
			// to avoid symmetries
			for (int i = 0; i < 6; ++i)
			{
				if (startingPosition >= 0 && i != startingPosition)
					continue;
				Board b;
				InitBoard(&b);

				b.xmax = b.xmin = offsets[CENTER].x;
				b.ymax = b.ymin = offsets[CENTER].y;
				PlayOne(&b, CENTER + sp1[i]);
				PlayOne(&b, CENTER + sp2[i]);
				PlayMove(&b, CENTER, 2);
				StoreGrid(&b, 2);
			}
			currentLevel = 2;
		}
	}
	time(&startTime);
	for (;;)
	{
		//printf("Compute %d\n", currentLevel);
		++currentLevel;
		int count = Compute(currentLevel);
		if (count == 0)
			break;

		if (end != 0 && currentLevel >= end)
		{
			printf("End at %d %d\n", currentLevel, end);

			break;
		}
	}
	time_t curTime;
	time(&curTime);
	FILE* out = fopen(GetSaveLog(), "at");
	fprintf(out, "%d %ds: X=%d-%d Y=%d-%d F=%d/%d %f %f %f %f\n", currentLevel, (int)(curTime - startTime), totalMinimalX, totalMaximalX, totalMinimalY, totalMaximalY, totalMaxFrontiers, MAXFRONT, Coef1, Coef2, Coef3, Coef4);
	fclose(out);
	return currentLevel;
}

static void UseRandomCoefficients(void)
{
	Coef1 = GET_FLOATRAND() * 100 - 20;
	Coef2 = GET_FLOATRAND() * 100 - 20;
	Coef3 = GET_FLOATRAND() * 100 - 20;
	Coef4 = GET_FLOATRAND() * 100 - 20;
	if (Coef1 < 0) Coef1 = 0;
	if (Coef2 < 0) Coef2 = 0;
	if (Coef3 < 0) Coef3 = 0;
	if (Coef4 < 0) Coef4 = 0;
}

int main(int argc, char** argv)
{
	printf("Size=%d\n", (int)sizeof(Board));
	char* loadFile = NULL;
	int start = 0;
	int end = 0;
	int startingPosition = -1;
	if (argc < 2)
	{
		printf("Syntaxes:\n");
		printf("run totalsize: beam search upto NN\n");
		printf("run totalsize startpos: beam search upto NN\n");
		printf("run totalsize filetoload start: beam search with file\n");
		printf("run totalsize filetoload start end: beam search with file\n");
		return 1;
	}

	totalMaxFrontiers = 0;
	totalMinimalX = totalMinimalY = GRID_SIZE;
	totalMaximalX = totalMaximalY = 0;
	ChangeCurrentProcessPriority();
	HashPrime = NextLowestPrime(atoi(argv[1]));

	for (int y = 0; y < GRID_SIZE; ++y)
	{
		for (int x = 0; x < GRID_SIZE; ++x)
		{
			int o = y * GRID_SIZE + x;
			offsets[o].x = x;
			offsets[o].y = y;
		}
	}
	grids1 = (Board*)malloc(HashPrime * sizeof(Board));
	if (grids1 == NULL)
	{
		printf("No memory\n");
		exit(0);
	}
	grids2 = (Board*)malloc(HashPrime * sizeof(Board));
	if (grids2 == NULL)
	{
		printf("No memory\n");
		exit(0);
	}

	// slow, so we only do it once!
	initZobrist();
#if USE_PATTERN
	PreparePattern();
#endif
	if (argc > 3)
	{
		loadFile = argv[2];
		if (argc >= 4)
		{
			start = atoi(argv[3]);
			if (argc >= 5)
			{
				end = atoi(argv[4]);
			}
		}
		printf("LoadFile=%s\n", loadFile);
		printf("Start=%d\n", start);
		printf("End=%d\n", end);
		UseRandomCoefficients();
		return PerformComputation(loadFile, start, end, -1);
	}

	if (argc == 3)
	{
		startingPosition = atoi(argv[2]);
	}

#if INFINITERUN
	for (;;)
	{
		++iteration;
		// to randomize more, we randomize the hash size !!!
		//HashPrime = NextLowestPrime(HashPrime - (GET_RAND32() % 8));
		HashPrime = NextLowestPrime(HashPrime);
		UseRandomCoefficients();
		PerformComputation(NULL, 0, 0, startingPosition);
	}
#else
	//Coef1 = 1;
	//for (Coef1 = -100; Coef1 <= 100; Coef1 += 50)
	for (Coef1 = -1; Coef1 <= 2; Coef1 += 1)
	{
		for (Coef2 = -1; Coef2 <= 2; Coef2 += 1)
		{
			for (Coef3 = -1; Coef3 <= 2; Coef3 += 1)
			{
				for (Coef4 = -1; Coef4 <= 2; Coef4 += 1)
				{
					PerformComputation(NULL, 0);
				}
			}
		}
	}
#endif

	free(grids2);
	free(grids1);
	return 0;
}
