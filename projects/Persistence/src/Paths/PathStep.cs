namespace Pulsas.Persistence.Paths;

public class PathStep
{
    public required Guid Id { get; set; }
    public Guid ContentId { get; set; }
    public Guid? ParentStepId { get; set; }
    public short Position { get; set; }
    public short Width { get; set; }
    public required byte[] Prefix { get; set; }
    public bool CandidatesInitialized { get; set; }
}
