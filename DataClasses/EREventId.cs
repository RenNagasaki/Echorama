using Echorama.Enums;
using FFXIVClientStructs.FFXIV.Common.Math;

namespace Echorama.DataClasses
{
    public class EREventId
    {
        public static int LastId = 0;
        public int Id { get; set; }
        public QueueStatus Status { get; set; } = QueueStatus.Created;
        public string PanoramaPath { get; set; }
        public string PanoramaName { get; set; }
        public string ActiveTask {get; set; }
        public int ImageCountH {get; set; }
        public int ImageCountV {get; set; }
        public double Fov;
        public Vector3 PanoramaLocation {get; set; } = Vector3.Zero;
        public float VerticalAngle {get; set; } = 30f;
        public float HorizontalAngle {get; set; } = 45f;
        public int Anchor {get; set; } = 19;

        public EREventId()
        {
            LastId++;
            this.Id = LastId;
        }
    }
}
