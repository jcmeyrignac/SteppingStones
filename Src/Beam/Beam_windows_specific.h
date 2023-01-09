//#pragma once
#include <windows.h>
#include <immintrin.h>
#define DEFAULT_PRIORITY 1	// default priority (1=lowest)

#if THREADS > 1
#include <process.h>
#include <tchar.h>
#endif

#if THREADS > 1
//static volatile long nbThreads;
static HANDLE threads[THREADS];
//static volatile HANDLE ready = 0;
static const volatile Board* secure, * endsecure;
#endif

#if THREADS > 1
static void CreateLock(volatile char* lock)
{
//	int locks = 0;
	while (_InterlockedExchange8(lock, 1))
	{
//		++locks;
		_mm_pause();
	}
//	return locks;
}

static void ReleaseLock(volatile char* lock)
{
	*lock = 0;
}
#endif

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


#if THREADS > 1
static int Table[THREADS];

static unsigned int WINAPI DistributedComputation(void* param)
{
	int currentThread = *(int*)param;
	assert(currentThread < THREADS);
	Board* b2 = &WorkUnit[currentThread].b2;
	for (;;)
	{
		Board* r = (Board*)_InterlockedExchangeAdd64((volatile long long*)&secure, sizeof(Board));
		if (r >= endsecure) break;
		if (r->lock)
		{
			printf("Bug !!!!\n");
			exit(100);
		}
		if (r->numberOfStones == 0) continue;
		PlayAllMoves(r, r->numberOfStones + 2, b2);
	}
	_endthreadex(0);
	//if (_InterlockedDecrement(&nbThreads) == 0)
	//	SetEvent(ready);
	return 0;
}

static void DistributeComputation(void)
{
	//TCHAR mutex[256];
	//_stprintf(mutex, TEXT("%lld"), __rdtsc());
	//ready = CreateEvent(NULL, TRUE, FALSE, mutex);
	//if (ERROR_ALREADY_EXISTS == GetLastError())
	//{
	//	printf("unable to create event (%d)\n", (int)GetLastError());
	//	exit(1010);
	//}

	secure = read;
	endsecure = read + HashPrime;
	//nbThreads = THREADS;
	for (int i = 0; i < THREADS; ++i)
	{
		Table[i] = i;
		threads[i] = (HANDLE)_beginthreadex(NULL, 0, DistributedComputation, &Table[i], CREATE_SUSPENDED, NULL);
		SetPriority(threads[i], i + 1, DEFAULT_PRIORITY);
		ResumeThread(threads[i]);
	}
	//WaitForSingleObject(ready, INFINITE);
	for (int i = 0; i < THREADS; ++i)
	{
		WaitForSingleObject(threads[i], INFINITE);
		CloseHandle(threads[i]);
	}
	//CloseHandle(ready);
}
#endif


static void ChangeCurrentProcessPriority(void)
{
	SetPriority(GetCurrentThread(), -1, DEFAULT_PRIORITY);
}
