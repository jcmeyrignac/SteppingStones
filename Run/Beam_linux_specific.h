//#pragma once
#include <immintrin.h>
#include <pthread.h>
#define max(a,b) (((a) > (b)) ? (a) : (b))
#define min(a,b) (((a) < (b)) ? (a) : (b))

#if THREADS > 1
static volatile long nbThreads;
static const volatile Board* secure, * endsecure;
#endif

#if THREADS > 1
static void CreateLock(volatile char* lock)
{
	// https://gcc.gnu.org/onlinedocs/gcc-4.1.0/gcc/Atomic-Builtins.html
	while (__sync_lock_test_and_set(lock, 1))
		while (*lock) {
		}
}

static void ReleaseLock(volatile char* lock)
{
	*lock = 0;
}
#endif

// OK!!!
static unsigned int GET_RAND32(void)
{
	unsigned int local;
#if 0
	CreateLock(&lockRandom);
	unsigned long long t, a = 698769069ULL;
	jkiss_x = 69069 * jkiss_x + 12345;
	jkiss_y ^= (jkiss_y << 13); jkiss_y ^= (jkiss_y >> 17); jkiss_y ^= (jkiss_y << 5); /* y must never be set to zero! */
	t = a * jkiss_z + jkiss_c; jkiss_c = (t >> 32); /* Also avoid setting z=c=0! */
	jkiss_z = (unsigned int)t;
	local = jkiss_x + jkiss_y + jkiss_z;
	ReleaseLock(&lockRandom);
	return local;
#else
	_rdrand32_step(&local);
	return local;
#endif
}

#if THREADS > 1
static int Table[THREADS];

static void *DistributedComputation(void* param)
{
	int currentThread = *(int*)param;
	assert(currentThread < THREADS);
	Board* b2 = &WorkUnit[currentThread].b2;
	for (;;)
	{
		Board* r = (Board*)__sync_fetch_and_add((volatile long long*)&secure, sizeof(Board));
		if (r >= endsecure) break;
		if (r->lock)
		{
			printf("Bug !!!!\n");
			exit(100);
		}
		if (r->numberOfStones == 0) continue;
		PlayAllMoves(r, r->numberOfStones + 2, b2);
	}
	return NULL;
}

#include <sched.h>
#ifndef SCHED_IDLE
#define SCHED_IDLE 5
#endif

static void DistributeComputation()
{
	pthread_t threads[THREADS];

	secure = read;
	endsecure = read + HashPrime;

	struct sched_param param;
	param.sched_priority = 0;
	for (int i = 0; i < THREADS; ++i)
	{
		Table[i] = i;
		int result = pthread_create(&threads[i], NULL, &DistributedComputation, &Table[i]);
		assert(!result);
		// change the priority
		result = pthread_setschedparam(threads[i], SCHED_IDLE, &param);
		assert(!result);
	}

	for (int i = 0; i < THREADS; ++i)
	{
		int result = pthread_join(threads[i], NULL);
		assert(!result);
	}
}
#endif

static void ChangeCurrentProcessPriority()
{
	//pthread_self()
	// nothing
}
