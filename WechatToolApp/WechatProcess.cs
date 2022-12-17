using System.Diagnostics;

namespace WechatToolApp.App
{
    public class WechatProcess
    {
        public Process Proc { get; set; }

        public bool MutexClosed { get; set; }

        public WechatProcess(Process p)
        {
            Proc = p;
            MutexClosed = false;
        }
    }
}
