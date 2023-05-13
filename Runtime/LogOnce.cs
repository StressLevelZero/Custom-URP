using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LogOnce
{
    private static LogOnce s_Instance;

    public static LogOnce Instance
    {
        get { 
            if (s_Instance == null) 
                s_Instance = new LogOnce();
            return s_Instance; 
        }
    }

    long m_messageFlags = 0;

    public void Print(string message, int messageNum)
    {
        long messageFlag = (1L << messageNum);
        if ((m_messageFlags & messageFlag) == 0)
        {
            m_messageFlags = m_messageFlags | messageFlag;
            Debug.Log(message);
        }
    }
}
