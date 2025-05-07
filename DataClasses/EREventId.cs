namespace Echorama.DataClasses
{
    public class EREventId
    {
        public static int LastId = 0;
        public int Id { get; set; }

        public EREventId()
        {
            LastId++;
            this.Id = LastId;
        }
    }
}
