/*
- parametrize NN and REDUCE
- add Compute2 folder to collect
- add heuristic: next16

*/

#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>		// also defines min/max!
#include <time.h>
#include <string.h>
#include <assert.h>

#include <windows.h>

//#define DEPTH_HEURISTIC 2
#define OPTIMIZER 1

//#define PARTIAL 1

#define GRID_SIZE 128
#ifndef REFILL
#define REFILL 0
#endif
#ifndef PARTIAL
#define PARTIAL 0
#endif

#define UPPER_N 33
#define UPPERB 300 //upper bound on solution
#define MAXFRONT (UPPERB*6)
#define AREA (GRID_SIZE * GRID_SIZE)

#define direction0 (-GRID_SIZE - 1)
#define direction1 (-GRID_SIZE)
#define direction2 (-GRID_SIZE + 1)
#define direction3 (-1)
#define direction4 (1)
#define direction5 (GRID_SIZE - 1)
#define direction6 (GRID_SIZE)
#define direction7 (GRID_SIZE + 1)

#define MASK unsigned int
#define MASK_SIZE (sizeof(MASK)*8)
#define ROUND_MASK(x) (((x) + (MASK_SIZE-1))/MASK_SIZE)
#define SET_BIT(M,B) (M[(B)/MASK_SIZE] |= (1U<<((B)%MASK_SIZE)))
//#define XOR_BIT(M,B) (M[(B)/MASK_SIZE] ^= (1U<<((B)%MASK_SIZE)))
//#define CLEAR_BIT(M,B) (M[(B)/MASK_SIZE] &= ~(1U<<((B)%MASK_SIZE)))
#define IS_BIT_SET(M,B)  (M[(B)/MASK_SIZE] & (1U<<((B)%MASK_SIZE)))

typedef struct
{
	MASK isFrontier[ROUND_MASK(AREA)];
	MASK isFilled[ROUND_MASK(AREA)];
	unsigned short numberOfStones;
	unsigned short numberOfOnes;			// number of one cells left to place
	unsigned short ones[UPPER_N];
	unsigned short stones[UPPERB];
	unsigned short nbFrontiers;			// history upper index of frontier cells
	unsigned short frontier[MAXFRONT];		// adjacent empty cells
	short csum[AREA];
	//short cvalue[AREA];
} Board;

int N;
time_t startTime;
Board levels[UPPERB];
int deepestLevel = 0;

#if PARTIAL
int forcedPositions[UPPERB];
#endif

#include "records.h"

#define DEFAULT_PRIORITY 1	// default priority (1=lowest)
static void SetPriority(HANDLE thread, int cpuNumber, int priority)
{
	if (cpuNumber >= 1 && cpuNumber <= 64)
	{
		SetThreadAffinityMask(thread, 1LL << (cpuNumber - 1));
	}
	SetPriorityClass(thread,
		(priority < 2 || priority > 6) ?
		NORMAL_PRIORITY_CLASS :
		IDLE_PRIORITY_CLASS);

	SetThreadPriority(thread,
		(priority == 1) ? THREAD_PRIORITY_IDLE :
		(priority == 2 || priority == 7) ? THREAD_PRIORITY_LOWEST :
		(priority == 3 || priority == 8) ? THREAD_PRIORITY_BELOW_NORMAL :
		(priority == 4 || priority == 9) ? THREAD_PRIORITY_NORMAL :
		(priority == 5 || priority == 10) ? THREAD_PRIORITY_ABOVE_NORMAL :
		THREAD_PRIORITY_HIGHEST);
}

static void InitBoard(Board* b)
{
	// clears everything!
	memset(b, 0, sizeof(Board));

	for (int i = 0; i < GRID_SIZE; ++i)
	{
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

static void savetodisk(Board* b)
{
	unsigned short cells[AREA];
	memset(cells, 0, sizeof(cells));
	int col, row;
	char filename[256];


	for (int i = 0; i < b->numberOfOnes; ++i)
		cells[b->ones[i]] = 1;
	for (int i = 0; i < b->numberOfStones; ++i)
		cells[b->stones[i]] = i + 2;

	sprintf(filename, "output%02d-%03d.txt", b->numberOfOnes, b->numberOfStones + 1);
	FILE* out = fopen(filename, "at");
	//find range of x and y values filled in
	int p1;
	int xmax, xmin;
	int ymax, ymin;
	xmax = ymax = -1;
	xmin = ymin = 1 << 30;

	for (p1 = 0; p1 < AREA; ++p1)
	{
		if (cells[p1] != 0)
		{
			int xx = p1 % GRID_SIZE;
			int yy = p1 / GRID_SIZE;
			xmin = min(xmin, xx);
			ymin = min(ymin, yy);
			xmax = max(xmax, xx);
			ymax = max(ymax, yy);
		}
	}

	for (row = ymin; row <= ymax; ++row)
	{
		for (col = xmin; col <= xmax; ++col)
		{
			fprintf(out, "%03d ", cells[row * GRID_SIZE + col]);
		}
		fprintf(out, "\n");
	}
	fprintf(out, "%d,%d\n", b->numberOfOnes, b->numberOfStones + 1);
	//new Al's format:
	for (int y = ymin; y <= ymax; ++y)
	{
		int spaces = 0;
		int comma = 0;
		if (y > ymin)
			fprintf(out, ",");

		fprintf(out, "(");
		for (int x = xmin; x <= xmax; ++x)
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

#define makeMoveMacro(source, dir, content) {\
	int p3 =  source + dir;\
	assert(p3 >= 0 && p3 < AREA);\
	b->csum[p3] += content;\
	if (!IS_BIT_SET(b->isFrontier, p3))\
	{\
		assert(!IS_BIT_SET(b->isFilled, p3));\
		b->frontier[b->nbFrontiers++] = p3;\
		SET_BIT(b->isFrontier, p3);\
	}\
}

static Board* CopyBoard(int current)
{
	// copy board of level "current" to "current+1"
	levels[current + 1] = levels[current];
	return &levels[current + 1];
}

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
	//b->cvalue[p1] = current;
	makeMoveMacro(p1, direction0, current);
	makeMoveMacro(p1, direction1, current);
	makeMoveMacro(p1, direction2, current);
	makeMoveMacro(p1, direction3, current);
	makeMoveMacro(p1, direction4, current);
	makeMoveMacro(p1, direction5, current);
	makeMoveMacro(p1, direction6, current);
	makeMoveMacro(p1, direction7, current);
}

#define makeMoveMacroWithoutBorder(source, dir, content) {\
	int p3 =  source + dir;\
	assert(p3 >= 0 && p3 < AREA);\
	b->csum[p3] += content;\
}
static void PlayOne(Board* b, int p1)
{
	assert(p1 >= 0 && p1 < AREA);
	assert(!IS_BIT_SET(b->isFilled, p1));
	SET_BIT(b->isFilled, p1);
	SET_BIT(b->isFrontier, p1);

	b->ones[b->numberOfOnes] = p1;
	++b->numberOfOnes;
	SET_BIT(b->isFilled, p1);
	SET_BIT(b->isFrontier, p1);
	//b->cvalue[p1] = 1;
	makeMoveMacroWithoutBorder(p1, direction0, 1);
	makeMoveMacroWithoutBorder(p1, direction1, 1);
	makeMoveMacroWithoutBorder(p1, direction2, 1);
	makeMoveMacroWithoutBorder(p1, direction3, 1);
	makeMoveMacroWithoutBorder(p1, direction4, 1);
	makeMoveMacroWithoutBorder(p1, direction5, 1);
	makeMoveMacroWithoutBorder(p1, direction6, 1);
	makeMoveMacroWithoutBorder(p1, direction7, 1);
}

#define getMovesMacro(xx) {\
	int p2 = p1 + xx;\
	if (!IS_BIT_SET(b->isFrontier, p2))\
	{\
		Board *b2 = CopyBoard(current); \
		PlayOne(b2, p2);\
		PlayMove(b2, p1, current);\
		recursive(current+1);\
	}\
}

time_t oldtime;
int CurrentSolution;
int CountSaved = 0;

static void recursive(int current)
{
	time_t current_time;
	time(&current_time);
	if (current_time != oldtime)
	{
		oldtime = current_time;
		printf("%d: %d %d %d\r", CurrentSolution, (int)(current_time - startTime), current, CountSaved);
	}

	Board* b = &levels[current];
	if (current - 1 >= records[b->numberOfOnes])
	{
		records[b->numberOfOnes] = current - 1;
		++CountSaved;
		savetodisk(b);
	}
	deepestLevel = max(deepestLevel, current);

#if PARTIAL
	int pp = forcedPositions[current];
	if (pp != 0)
	{
		// we have a forced move
		if (b->csum[pp] != current)
			return;

		Board* b2 = CopyBoard(current);
		PlayMove(b2, pp, current);

		// we check that the sums are consistent
		// a sum cannot decrease
		for (int i = current + 1; i < UPPERB; ++i)
		{
			int pp1 = forcedPositions[i];
			if (pp1 != 0 && b2->csum[pp1] > i)
			{
				return;
			}
		}
		recursive(current + 1);
		return;
	}

#endif

#if DEPTH_HEURISTIC
	// heuristic
	//if (b->numberOfOnes >= N - DEPTH_HEURISTIC * 2 && current < records[b->numberOfOnes - DEPTH_HEURISTIC])
	if (b->numberOfOnes >= N - DEPTH_HEURISTIC * 2 && current < records[b->numberOfOnes - DEPTH_HEURISTIC])
		return;
#endif

	for (int f = b->nbFrontiers - 1; f >= 0; --f)
		//for (int f = 0; f < b->nbFrontiers; ++f)
	{
		int p1 = b->frontier[f];
		assert(p1 >= 0 && p1 < AREA);
		if (!IS_BIT_SET(b->isFilled, p1))
		{
			if (b->csum[p1] == current)
			{
				//add number alone
				Board* b2 = CopyBoard(current);
				PlayMove(b2, p1, current);
				recursive(current + 1);
			}
			if (b->csum[p1] == current - 1 && b->numberOfOnes < N)
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
	//#if USE_HEURISTIC
	//	int count = 0;
	//	for (int f = 0; f < b->nbFrontiers; ++f)
	//	{
	//		int p1 = b->frontier[f];
	//		if (b->cell[p1].value == 0 && b->cell[p1].csum == current)
	//		{
	//			//add number alone
	//			Board* b2 = CopyBoard(current);
	//			PlayMove(b2, p1, current);
	//			recursive(current + 1);
	//			++count;
	//		}
	//	}
	//	if (count)
	//		return;
	//	if (/*count == 0 &&*/ b->numberOfOnes < N)
	//	{
	//		for (int f = 0; f < b->nbFrontiers; ++f)
	//		{
	//			int p1 = b->frontier[f];
	//			if (b->cell[p1].value == 0 && b->cell[p1].csum == current - 1)
	//			{
	//				//add number and a 1
	//				getMovesMacro(direction0);
	//				getMovesMacro(direction1);
	//				getMovesMacro(direction2);
	//				getMovesMacro(direction3);
	//				getMovesMacro(direction4);
	//				getMovesMacro(direction5);
	//				getMovesMacro(direction6);
	//				getMovesMacro(direction7);
	//			}
	//		}
	//	}
	//#else
	//#endif
}


typedef struct
{
	int x, y;
} Point;

static void StoreSolution(int values[AREA])
{
	int maxval = 0;
	Point playpos[UPPERB];
	Point ones[UPPER_N];
	int nbOnes = 0;
	int xmax = 0;
	int ymax = 0;

	for (int i = 0; i < UPPERB; ++i)
	{
		playpos[i].x = 0;
		playpos[i].y = 0;
	}
	//for (int i = 0; i < UPPER_N; ++i)
	//{
	//	ones[i].x = 0;
	//	ones[i].y = 0;
	//}

	for (int y = 0; y < GRID_SIZE; ++y)
	{
		for (int x = 0; x < GRID_SIZE; ++x)
		{
			int o = y * GRID_SIZE + x;
			int val = values[o];
			if (val > 1)
			{
				playpos[val].x = x;
				playpos[val].y = y;
			}
			else if (val == 1)
			{
				ones[nbOnes].x = x;
				ones[nbOnes].y = y;
				++nbOnes;
			}
			if (val != 0)
			{
				maxval = max(maxval, val);
				xmax = max(xmax, x);
				ymax = max(ymax, y);
			}
		}
	}

	if (maxval < 2)
		return;

	// this forces the 2 at the center
	int startx = GRID_SIZE / 2 - playpos[2].x;
	int starty = GRID_SIZE / 2 - playpos[2].y;

#if PARTIAL
	Board* b = &levels[2];
	InitBoard(b);

	for (int i = 0; i < nbOnes; ++i)
	{
		int pos = (ones[i].y + starty) * GRID_SIZE + (ones[i].x + startx);
		PlayOne(b, pos);
	}

	for (int move = 2; move <= maxval; ++move)
	{
		if (playpos[move].x == 0 && playpos[move].y == 0)
		{
			forcedPositions[move] = 0;
		}
		else
		{
			int pos = (playpos[move].y + starty) * GRID_SIZE + (playpos[move].x + startx);
			forcedPositions[move] = pos;
		}
	}
	recursive(2);
#elif !REFILL
	Board* b = &levels[maxval + 1];
	InitBoard(b);

	//#define CopyOne(dir) if (src + dir >= 0 && src + dir < AREA && b->cell[pos + dir].value == 0 && values[src + dir] == 1) PlayOne(b, pos + dir);
#define CopyOne(dir) if(src+dir>=0 && src+dir < AREA){ assert(pos+dir>=0 && pos+dir < AREA); if (!IS_BIT_SET(b->isFilled, pos + dir) && values[src + dir] == 1) {PlayOne(b, pos + dir);}}

	for (int move = 2; move <= maxval; ++move)
	{
		int pos = (playpos[move].y + starty) * GRID_SIZE + playpos[move].x + startx;
		assert(pos >= 0 && pos < AREA);
		assert(!IS_BIT_SET(b->isFilled, pos));
		if (b->csum[pos] == move)
		{
			PlayMove(b, pos, move);
		}
		else
		{
			int src = playpos[move].y * GRID_SIZE + playpos[move].x;
			CopyOne(direction0);
			CopyOne(direction1);
			CopyOne(direction2);
			CopyOne(direction3);
			CopyOne(direction4);
			CopyOne(direction5);
			CopyOne(direction6);
			CopyOne(direction7);
			if (b->csum[pos] != move)
				__debugbreak();
			PlayMove(b, pos, move);
		}
	}
	recursive(maxval + 1);
#else
	Board* b = &levels[maxval + 1];
	InitBoard(b);

	for (int i = 0; i < nbOnes; ++i)
	{
		int pos = (ones[i].y + starty) * GRID_SIZE + (ones[i].x + startx);
		PlayOne(b, pos);
	}

	for (int move = 2; move <= maxval; ++move)
	{
		int src = playpos[move].y * GRID_SIZE + playpos[move].x;
		int pos = (playpos[move].y + starty) * GRID_SIZE + (playpos[move].x + startx);
		if (b->csum[pos] != move)
		{
			printf("Incorrect solution\n");
			return;
		}
		PlayMove(b, pos, move);
	}
	recursive(maxval + 1);
#endif
}

/*
static void Load(const	char* filename)
{
	int x, y;
	FILE* fp;
	int values[AREA];
	int playposx[UPPERB], playposy[UPPERB];
	int maxval = 0;
	int xmax = 0, ymax = 0;

	fp = fopen(filename, "r");
	if (fp == NULL)
	{
		printf("Can't open file\n");
		return;
	}
	int countSolutions = 0;
	y = 0;
	memset(values, 0, sizeof(values));
	while (!feof(fp))
	{
		char line[1024];
		line[0] = 0;
		char* p = line;
		fgets(line, 1023, fp);
		//printf("%s", line);
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
				CurrentSolution = countSolutions;
				StoreSolution(maxval, xmax, ymax, values, playposx, playposy);
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
					printf("Error");
					exit(0);
				}

				if (x >= GRID_SIZE || y >= GRID_SIZE)
				{
					printf("Out of range");
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
		StoreSolution(maxval, xmax, ymax, values, playposx, playposy);
	}
	fclose(fp);

}
*/

static void Load(const char* filename)
{
	int x, y;
	FILE* fp;
	int values[AREA];

	fp = fopen(filename, "r");
	if (fp == NULL)
	{
		printf("Can't open file\n");
		return;
	}
	int countSolutions = 0;
	y = 0;
	memset(values, 0, sizeof(values));
	while (!feof(fp))
	{
		char line[1024];
		line[0] = 0;
		char* p = line;
		fgets(line, 1023, fp);
		//printf("%s", line);
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
				CurrentSolution = countSolutions;
				StoreSolution(values);
				memset(values, 0, sizeof(values));
			}
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
					printf("Error");
					exit(0);
				}

				if (x >= GRID_SIZE || y >= GRID_SIZE)
				{
					printf("Out of range");
					exit(0);
				}
				values[y * GRID_SIZE + x] = val;
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
		StoreSolution(values);
	}
	fclose(fp);

}


int main(int argc, const char** argv)
{
	if (argc < 2)
	{
		printf("Syntax: run filename N\n");
		printf("Example: run toto26 26\n");
		return 0;
	}

	SetPriority(GetCurrentThread(), -1, DEFAULT_PRIORITY);

	N = atoi(argv[2]);

	memset(levels, 0, sizeof(levels));
	//4th move for N=6, V4 -- put three above 2, do not check symmetrical equivalents
	time(&startTime);

	Load(argv[1]);

	time_t curTime;
	time(&curTime);
	FILE* out = fopen("logs.txt", "a");
	fprintf(out, "Done in %lld s\n", curTime - startTime);
	fclose(out);
	printf("Done in %lld s\n", curTime - startTime);
	return deepestLevel;
}
