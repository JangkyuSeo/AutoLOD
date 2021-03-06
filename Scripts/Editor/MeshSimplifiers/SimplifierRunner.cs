﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Unity.AutoLOD
{

    /// <summary>
    /// Sometimes simplifer not running when create lots of backgroud workers. ( e.g., SimplygonMeshSimplifer )
    /// This helps to didn't make too many background wokers.
    /// Runs while maintaining a specified number of background wokers.
    ///
    /// 
    /// </summary>
    class SimplifierRunner : ScriptableSingleton<SimplifierRunner>
    {
        private const int k_MaxWorkerCount = 8;
        private const int k_WaitTerminateMS = 3000;

        void OnEnable()
        {
            
            m_Workers.Clear();
            m_SimplificationActions.Clear();
            m_CompleteActions.Clear();

            EditorApplication.update += EditorUpdate;

            m_IsWorking = true;
            for (int i = 0; i < k_MaxWorkerCount; ++i)
            {
                Thread thread = new Thread(Worker_DoWork);
                thread.Start();
                m_Workers.Add(thread);
            }
            
        }

        void OnDisable()
        {
            m_IsWorking = false;
            foreach (var worker in m_Workers)
            {
                if (worker.Join(k_WaitTerminateMS) == false)
                {
                    worker.Abort();
                }
            }

            EditorApplication.update -= EditorUpdate;

            m_Workers.Clear();
            m_SimplificationActions.Clear();
            m_CompleteActions.Clear();
        }

        public void Cancel()
        {
            m_SimplificationActions.Clear();
            OnDisable();
            OnEnable();
        }

        private void Worker_DoWork()
        {
            while (m_IsWorking)
            {
                if (m_SimplificationActions.Count == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                ActionContainer actionContainer;
                lock (m_SimplificationActions)
                {
                    //double check for empty in lock.
                    if (m_SimplificationActions.Count == 0)
                        continue;

                    actionContainer = m_SimplificationActions.Dequeue();
                }

                try
                {

                    Stack<IEnumerator> coroutineStack = actionContainer.CoroutineStack;

                    while (coroutineStack.Count > 0)
                    {
                        if (m_IsWorking == false)
                            return;

                        IEnumerator coroutine = coroutineStack.Peek();

                        if (coroutine.MoveNext())
                        {
                            if (coroutine.Current == null)
                            {
                                Thread.Sleep(20);
                                continue;
                            }

                            var next = coroutine.Current as IEnumerator;
                            if ( next != null)
                            {
                                coroutineStack.Push(next);
                            }
                        }
                        else
                        {
                            coroutineStack.Pop();
                        }
                    }

                    if (coroutineStack.Count == 0)
                    {
                        lock (m_CompleteActions)
                        {
                            m_CompleteActions.Enqueue(actionContainer.CompleteAction);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e is ThreadAbortException)
                        return;

                    Debug.LogError("Exception occur : " + e.Message);
                    continue;
                }

                
            }
        }
        private void EditorUpdate()
        {
            while (m_CompleteActions.Count > 0)
            {
                Action completeAction;
                lock (m_CompleteActions)
                {
                    if (m_CompleteActions.Count == 0)
                        return;

                    completeAction = m_CompleteActions.Dequeue();
                }

                completeAction();
            }
        }

        public void EnqueueSimplification(IEnumerator coroutine, Action completeAction)
        {
            lock (m_SimplificationActions)
            {
                ActionContainer container;
                container.CoroutineStack = new Stack<IEnumerator>();
                container.CompleteAction = completeAction;
                container.CoroutineStack.Push(coroutine);
                m_SimplificationActions.Enqueue(container);
            }
        }

        private struct ActionContainer
        {
            public Stack<IEnumerator> CoroutineStack;
            public Action CompleteAction;
        }


        private bool m_IsWorking = false;
        private List<Thread> m_Workers = new List<Thread>();

        private Queue<ActionContainer> m_SimplificationActions = new Queue<ActionContainer>();
        private Queue<Action> m_CompleteActions = new Queue<Action>();


    }
}
