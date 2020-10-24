using System.Collections.Generic;
using System.IO;

namespace MultiFunPlayer.Common
{
    public enum AxisFilesChangeType
    {
        Reset,
        Update
    }

    public class AxisFilesChangedMessage
    {
        public AxisFilesChangeType ChangeType { get; }
        public IDictionary<DeviceAxis, FileInfo> AxisFiles { get; }
        public AxisFilesChangedMessage(IDictionary<DeviceAxis, FileInfo> axisFiles, AxisFilesChangeType changeType)
        {
            AxisFiles = axisFiles;
            ChangeType = changeType;
        }
    }
}
