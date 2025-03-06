using System;

namespace SplenSoft.UnityUtilities
{
    [Serializable]
    public class AppQuitInTaskException : Exception
    {
        public AppQuitInTaskException() : base("The application quit while an asynchronous task was running.")
        { 
        }
    }
}
