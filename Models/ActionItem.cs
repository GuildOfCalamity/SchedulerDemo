using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SchedulerDemo;

/// <summary>
/// Our support class for <see cref="ScheduleList"/>.
/// </summary>
public class ActionItem
{
    public int Id { get; set; }
    public bool Activated { get; set; } = false;
    public Action? ToRun { get; set; }
    public DateTime RunTime { get; set; }
    public CancellationToken Token { get; set; }

    public ActionItem(int id, Action action, DateTime runTime, CancellationToken token = default(CancellationToken))
    {
        Id = id;
        ToRun = action;
        RunTime = runTime;
        Token = token;
    }
}
