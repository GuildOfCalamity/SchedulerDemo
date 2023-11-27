using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchedulerDemo;

public class FileData
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWrite { get; set; }
    public DateTime Created { get; set; }
    public FileAttributes Attributes { get; set; }
}
