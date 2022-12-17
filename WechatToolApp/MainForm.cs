using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace WechatToolApp.App
{
    public partial class MainForm : Form
    {
        const string WECHAT_NAME = "WeChat";
        const string WECHAT_EXE_NAME = "WeChat.exe";

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;                             //最左坐标
            public int Top;                             //最上坐标
            public int Right;                           //最右坐标
            public int Bottom;                        //最下坐标
        }


        [DllImport("kernel32.dll")]
        public static extern int WinExec(string exeName, int operType);


        [DllImportAttribute("user32.dll", EntryPoint = "MoveWindow")]
        public static extern bool MoveWindow(System.IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);


        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint Flags);

        public MainForm()
        {
            InitializeComponent();
        }

        private int appNum = 0;
        List<ProcessStartInfo> appThreads = new List<ProcessStartInfo>();

        private void checkAppThreads()
        {
            if (appThreads.Count > 0)
            {
                ProcessStartInfo thread = appThreads[0];
                new Thread(() =>
                {
                    Process.Start(thread);
                    appThreads.RemoveAt(0);
                }).Start();
            }
        }

        private List<Thread> threadsPool = new List<Thread>();

        private void clearThreadPool()
        {
            if (threadsPool.Count > 0)
            {
                threadsPool.ForEach(delegate (Thread thread)
                {
                    thread.Abort();
                });
                threadsPool.Clear();
            }

            if (appThreads.Count > 0)
            {
                appThreads.Clear();
            }
        }

        private void btnStartTimer_Click(object sender, EventArgs e)
        {
            clearThreadPool();

            if (!mutexHandleCloseTimer.Enabled)
            {
                mutexHandleCloseTimer.Stop();
            }

            btnStartTimer.Enabled = false;
            btnStopTimer.Enabled = true;
            btnSort.Enabled = true;

            if (!mutexHandleCloseTimer.Enabled)
            {
                mutexHandleCloseTimer.Start();
            }
            string appPath = PathUtil.FindInstallPathFromRegistry(WECHAT_NAME) + Path.DirectorySeparatorChar + WECHAT_EXE_NAME;
            if (!File.Exists(appPath))
            {
                MessageBox.Show("请先安装微信桌面App", "提示");
                return;
            }

            appNum = (int)numericUpDown.Value;
            for (int i = 0; i < appNum; i++)
            {
                appThreads.Add(new ProcessStartInfo(appPath));
            }
            checkAppThreads();
        }

        private void btnStopTimer_Click(object sender, EventArgs e)
        {
            clearThreadPool();
            closeAllMutex();
            mutexHandleCloseTimer.Stop();
            btnStartTimer.Enabled = true;
            btnStopTimer.Enabled = false;

            Process[] processes = Process.GetProcessesByName(WECHAT_NAME);
            if (processes.Length > 0)
            {
                foreach (Process p in processes)
                {
                    p.Kill();
                }
            }
            else
            {
                MessageBox.Show("当前无微信进程", "提示");
            }
        }

        private List<WechatProcess> wechatProcesses = new List<WechatProcess>();

        private void mutexHandleCloseTimer_Tick(object sender, EventArgs e)
        {
            Process[] processes = Process.GetProcessesByName(WECHAT_NAME);
            startNumTxt.Text = "启动数：" + processes.Length;
            if (processes.Length <= 0)
            {
                return;
            }

            // 添加新进程
            foreach (Process p in processes)
            {
                int i = 0;
                for (i = 0; i < wechatProcesses.Count; i++)
                {
                    WechatProcess wechatProcess = wechatProcesses[i];
                    if (wechatProcess.Proc.Id == p.Id)
                    {
                        break;
                    }
                }
                if (i == wechatProcesses.Count)
                {
                    wechatProcesses.Add(new WechatProcess(p));
                }
            }
            // 关闭所有存在互斥句柄的进程
            int num = 0;
            for (int i = wechatProcesses.Count - 1; i >= 0; i--)
            {
                WechatProcess wechatProcess = wechatProcesses[i];
                if (!wechatProcess.MutexClosed)
                {
                    wechatProcess.MutexClosed = ProcessUtil.CloseMutexHandle(wechatProcess.Proc);
                    Console.WriteLine("进程：" + wechatProcess.Proc.Id + ",关闭互斥句柄：" + wechatProcess.MutexClosed);

                    if (wechatProcess.MutexClosed)
                    {
                        checkAppThreads();
                    }

                }
                else
                {
                    if (wechatProcess.Proc.HasExited)
                    {
                        // 移除不存在的线程
                        wechatProcesses.RemoveAt(i);
                    }
                    else
                    {
                        num++;
                    }
                }
            }
        }

        private void closeAllMutex()
        {
            startNumTxt.Text = "启动数：0";
            Process[] processes = Process.GetProcessesByName(WECHAT_NAME);
            ProcessUtil.CloseMutexHandle(processes);
        }

        private void FormMultiInstance_FormClosed(object sender, FormClosedEventArgs e)
        {
            mutexHandleCloseTimer.Stop();
        }

        private void FormMultiInstance_Load(object sender, EventArgs e)
        {
            mutexHandleCloseTimer.Start();
            btnStopTimer.Enabled = true;
        }

        private bool isSortRunning = false;
        private void btnSort_Click(object sender, EventArgs e)
        {
            Process[] processes1 = Process.GetProcessesByName(WECHAT_NAME);
            if (processes1.Length == 0)
            {
                return;
            }

            int realAppNum = Math.Max(appNum, processes1.Length);

            if (isSortRunning) return;
            isSortRunning = true;
            btnSort.Enabled = false;

            if (threadsPool.Count > 0)
            {
                threadsPool.ForEach(delegate (Thread thread)
                {
                    thread.Abort();
                });
                threadsPool.Clear();
            }

            Thread startThread = new Thread(() =>
            {
                int maxW = 0;
                int maxH = 0;
                int i = 0;
                Hashtable hashtable = new Hashtable();
                bool flag = false;
                while (true)
                {
                    Thread.Sleep(1000 / 30);
                    Process[] processes = Process.GetProcessesByName(WECHAT_NAME);
                    if (processes.Length >= realAppNum)
                    {
                        for (i = 0; i < processes.Length; i++)
                        {
                            Process process = processes[i];
                            IntPtr awin = process.MainWindowHandle;    //获取当前窗口句柄
                            RECT rect = new RECT();
                            GetWindowRect(awin, ref rect);
                            int width = rect.Right - rect.Left;                     //窗口的宽度
                            int height = rect.Bottom - rect.Top;                   //窗口的高度

                            if (width > 0 && height > 0)
                            {
                                if (!hashtable.ContainsKey(process.Id))
                                {
                                    hashtable.Add(process.Id, process);
                                    maxW = width;
                                    maxH = height;
                                    if (hashtable.Count >= realAppNum)
                                    {
                                        flag = true;
                                        break;
                                    }
                                }
                            }
                        }

                        int screenW = SystemInformation.VirtualScreen.Width;
                        int screenH = SystemInformation.VirtualScreen.Height;
                        int itemW = maxW + 5;
                        int itemH = maxH + 5;
                        int column = screenW / itemW;
                        int row = screenH / itemH;

                        i = 0;
                        foreach (DictionaryEntry entry in hashtable)
                        {
                            if (i >= column * row)
                            {
                                i = 0;
                            }
                            Process process = (Process)entry.Value;
                            int initX = (i % column) * itemW;
                            int initY = (i / column) * itemH;

                            SetWindowPos(process.MainWindowHandle, 0, initX, initY, 0, 0, 1); //简单解释下就是第一个参数为你要控制的窗口，最后一个参数为控制位置生效还是大小生效等
                            i += 1;
                        }

                        if (flag)
                        {
                            isSortRunning = false;
                            Action<bool> AsyncUIDelegate = delegate (bool enable ) { 
                                btnSort.Enabled = enable;
                            };
                            btnSort.Invoke(AsyncUIDelegate, new object[] { true });
                            break;
                        }
                    }
                }
            });
            startThread.Start();
            threadsPool.Add(startThread);
        }
    }
}
