using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lzw.Demonstration;
public class LzwEncodingPausedEventArgs : EventArgs {
    public object Entry { get; }

    public LzwEncodingPausedEventArgs(object entry) => Entry = entry;
}
