#if UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Native window placement: top-right, borderless, top-most, with no hidden board area.</summary>
public static class WindowsWindow
{
    const uint SWP_NOSIZE=0x0001, SWP_NOACTIVATE=0x0010, SWP_FRAMECHANGED=0x0020;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    public static void MakeCompactTopMost()
    {
        IntPtr h = GetActiveWindow(); if (h == IntPtr.Zero) return;
        int width=430, height=820, screenWidth=GetSystemMetrics(0);
        SetWindowPos(h, HWND_TOPMOST, screenWidth-width-12, 8, width, height, SWP_NOACTIVATE|SWP_FRAMECHANGED);
    }
}
#endif
