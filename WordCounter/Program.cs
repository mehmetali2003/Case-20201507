using System;
using System.Collections.Generic;
using System.Threading;
using System.Configuration;
using System.IO;
using System.Linq;

namespace WordProcessor
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Int32.TryParse(ConfigurationManager.AppSettings["ThreadCount"], out int threadCount);
                if (threadCount <= 0)
                {
                    throw new Exception("Thread count has to be greater than zero!");
                }
                TaskQueue<string> taskQueue = new TaskQueue<string>(threadCount);
                List<string> sentences = GetSentences();
                for (int i = 0; i < sentences.Count; i++)
                {
                    taskQueue.EnqueueTask(sentences[i], i % threadCount);
                }

                taskQueue.Dispose();

                Console.WriteLine(string.Format("Sentence count: {0}", sentences.Count));
                Dictionary<string, long> wordCount = taskQueue.GetWordCount();
                long totalWordCount = wordCount.Sum(w => w.Value);
                Console.WriteLine(string.Format("Average word count: {0}", totalWordCount / (decimal)sentences.Count));
                Console.WriteLine();

                foreach (var item in wordCount)
                {
                    Console.WriteLine($"{item.Key}: {item.Value}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error: {0}", ex.Message));
            }
            finally
            {
                string command = Console.ReadLine();
                if (command == string.Empty)
                {
                    System.Environment.Exit(0);
                }
            }
        }

        static List<string> GetSentences()
        {
            string filePath = ConfigurationManager.AppSettings["FilePath"];
            string allText = string.Empty;
            try
            {
                allText = File.ReadAllText(filePath);
            }
            catch
            {
                throw new Exception("Could not find input file!");
            }

            return allText.Split(new char[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
        }
    }

    public class TaskQueue<T> : IDisposable where T : class
    {
        public Dictionary<string, long> wordCount = new Dictionary<string, long>();
        object locker = new object();
        Thread[] workers;
        List<Queue<T>> queueList = new List<Queue<T>>();
        List<object> queueLockers = new List<object>();

        public TaskQueue(int workerCount)
        {
            workers = new Thread[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                int tempIndex = i;
                queueList.Add(new Queue<T>());
                queueLockers.Add(new object());
                (workers[tempIndex] = new Thread(() => Consume(tempIndex))).Start();
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < workers.Length; i++)
            {
                int tempIndex = i;
                EnqueueTask(null, tempIndex);
            }

            for (int i = 0; i < workers.Length; i++)
            {
                int tempIndex = i;
                workers[tempIndex].Join();
            }
        }

        public void EnqueueTask(T task, int queueIndex)
        {
            lock (queueLockers[queueIndex])
            {
                queueList[queueIndex].Enqueue(task);
                Monitor.PulseAll(queueLockers[queueIndex]);
            }
        }

        void Consume(int queueIndex)
        {
            Queue<T> taskQ = queueList[queueIndex];
            object queueLocker = queueLockers[queueIndex];
            while (true)
            {
                T task;
                lock (queueLocker)
                {
                    while (taskQ.Count == 0) Monitor.Wait(queueLocker);
                    task = taskQ.Dequeue();
                }
                if (task == null) return;

                string sentence = (string)(object)task;
                string[] words = sentence.Split(new string[] { " ", Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();

                foreach (var word in words)
                {
                    lock (locker)
                    {
                        if (wordCount.ContainsKey(word))
                        {
                            wordCount[word]++;
                        }
                        else
                        {
                            wordCount.Add(word, 1);
                        }
                    }
                }
            }
        }

        public Dictionary<string, long> GetWordCount()
        {
            return wordCount.OrderByDescending(i => i.Value).ToDictionary(d => d.Key, d => d.Value);
        }
    }
}
