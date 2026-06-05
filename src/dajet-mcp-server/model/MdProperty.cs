namespace DaJet.Mcp.Model
{
    public sealed class MdProperty
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Purpose { get; set; } = string.Empty;

        public List<MdColumn> Columns { get; set; } = new();

        public List<Guid> References { get; set; } = new();
    }
}