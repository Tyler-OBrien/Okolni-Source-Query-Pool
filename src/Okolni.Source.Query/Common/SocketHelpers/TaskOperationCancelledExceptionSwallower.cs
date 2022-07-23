using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Okolni.Source.Query.Common.SocketHelpers
{
    public static class TaskOperationCancelledExceptionSwallower
    {
        public static Task HandleOperationCancelled(this Task task)
        {
            task.ContinueWith(task =>
            {
                if (task.Exception is { InnerException: not OperationCanceledException })
                {
                    throw task.Exception;
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }
        public static Task<T> HandleOperationCancelled<T>(this Task<T> task)
        {
            task.ContinueWith(task =>
            {
                if (task.Exception is { InnerException: not OperationCanceledException })
                {
                    throw task.Exception;
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }
    }
}
