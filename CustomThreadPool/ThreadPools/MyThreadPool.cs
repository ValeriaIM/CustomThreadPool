using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomThreadPool
{
    public class MyThreadPool : IThreadPool
    {
        private long processedTask = 0L;
        public void EnqueueAction(Action action)
        {
            ThreadWorker.AddAction(delegate
            {
                action.Invoke();
                Interlocked.Increment(ref processedTask);
            });
        }

        public long GetTasksProcessedCount() => processedTask;
    }

    public static class ThreadWorker
    {
        private static Queue<Action> queue = new Queue<Action>();
        private static Dictionary<int, WorkStealingQueue<Action>> _actionsDict = new Dictionary<int, WorkStealingQueue<Action>>();
        
        static ThreadWorker()
        {
            void Worker()
            {
                while (true)
                {
                    Action newAction = delegate { };
                    while (_actionsDict[Thread.CurrentThread.ManagedThreadId].LocalPop(ref newAction))
                        newAction.Invoke();

                    var tryFlag = IsDequeued(true);

                    if (!tryFlag)
                        tryFlag = TryStealActionPool(tryFlag);
                    if (!tryFlag)
                        TryDequeueElseWait();
                }
            }

            StartBckThreads(Worker, 10);
        }

        private static bool IsDequeued(bool flag)
        {
            lock (queue)
            {
                if (queue.TryDequeue(out var action))
                    _actionsDict[Thread.CurrentThread.ManagedThreadId].LocalPush(action);
                else
                    flag = false;
            }
            return flag;
        }

        private static bool TryStealActionPool(bool flag)
        {
            foreach (var threadPool in _actionsDict)
            {
                Action action = delegate { };
                if (!threadPool.Value.TrySteal(ref action)) continue;
                _actionsDict[Thread.CurrentThread.ManagedThreadId].LocalPush(action);
                flag = true;
                return true;
            }
            return flag;
        }

        private static void TryDequeueElseWait()
        {
            lock (queue)
            {
                if (queue.TryDequeue(out var action))
                    _actionsDict[Thread.CurrentThread.ManagedThreadId].LocalPush(action);
                else
                    Monitor.Wait(queue);
            }
        }

        public static void AddAction(Action action)
        {
            lock (queue)
            {
                queue.Enqueue(action);
                Monitor.Pulse(queue);
            }
        }

        private static Thread[] StartBckThreads(Action action, int count)
        {
            var threads = new List<Thread>();
            for (var i = 0; i < count; i++)
                threads.Add(StartBckThread(action));
            return threads.ToArray();
        }

        private static Thread StartBckThread(Action action)
        {
            var thread = new Thread(() => action()) 
            { 
                IsBackground = true 
            };
            _actionsDict[thread.ManagedThreadId] = new WorkStealingQueue<Action>();
            thread.Start();
            return thread;
        }
    }
}
