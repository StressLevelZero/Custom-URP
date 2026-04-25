using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SLZ
{
    public class SortedEvent<T> where T : struct
    {
        SortedList<T, Action> eventDictionary;
    
        public void Register(Action action, T order)
        {
            if (eventDictionary == null) eventDictionary = new SortedList<T, Action>();
    
            if (eventDictionary.ContainsKey(order))
            {
                eventDictionary[order] += action;
            }
            else
            {
                eventDictionary.Add(order, action);
            }
        }
    
        public void Unregister(Action action, T order)
        {
            if (eventDictionary == null) return;
    
            if (eventDictionary.ContainsKey(order))
            {
                eventDictionary[order] -= action;
                if (eventDictionary[order].GetInvocationList().Length == 0)
                {
                    eventDictionary.Remove(order);
                }
            }
        }
    
        public void GarbageCollect()
        {
            if (eventDictionary == null) return;
    
            List<T> removeKeys = new List<T>(eventDictionary.Count);
            List<KeyValuePair<T, List<Action>>> removeActions = new List<KeyValuePair<T, List<Action>>>(eventDictionary.Count);
            foreach (KeyValuePair<T, Action> pair in eventDictionary)
            {
                if (pair.Value == null)
                {
                    removeKeys.Add(pair.Key);
                    continue;
                }
                Delegate[] delegates = pair.Value.GetInvocationList();
                int numDelegates = delegates.Length;
                int numValidDelegates = delegates.Length;
                int numRemoved = 0;
                List<Action> removeAction = new List<Action>();
                for (int dIdx = numValidDelegates - 1; dIdx >= 0; dIdx--)
                {
                    Delegate d = delegates[dIdx];
                    if (d.Method.IsStatic) continue;
                    
                    // Destroyed unity objects aren't actually null, `== null` is overloaded to pretend that they are.
                    // Checking for null equality when the object is boxed will return false even if the object has been 
                    // destroyed since the overload is not used. Instead, cast to a UnityEngine.Object and cast it to 
                    // bool to check for destruction
                    if ((d.Target is UnityEngine.Object o) && !o)
                    {
                        removeAction.Add(pair.Value);
                        numValidDelegates--;
                        continue;
                    }
                    if (d.Target == null)
                    {
                        removeAction.Add(pair.Value);
                        numValidDelegates--;
                        continue;
                    }
                }
    
                if (numValidDelegates <= 0)
                {
                    removeKeys.Add(pair.Key);
                }
                else if (removeAction.Count > 0)
                {
                    removeActions.Add(new KeyValuePair<T, List<Action>>(pair.Key, removeAction));
                }
                
            }
    
            foreach (T key in removeKeys)
            {
                eventDictionary.Remove(key);
            }
            foreach (KeyValuePair<T, List<Action>> pair in removeActions)
            {
                Action baseAction = eventDictionary[pair.Key];
                foreach (Action subAction in pair.Value)
                {
                    baseAction -= subAction;
                }
                eventDictionary[pair.Key] = baseAction;
            }
        }
    
        public void Invoke()
        {
            if (eventDictionary == null) return;
    
            foreach (KeyValuePair<T, Action> pair in eventDictionary)
            {
                try
                {
                    pair.Value?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
