namespace museum_api_sysprog_1.Entities
{
    public class Painting
    {
        public int objectID { get; set; }
        public bool isHighlight { get; set; }
        public string accessionNumber { get; set; } = string.Empty;
        public string accessionYear { get; set; } = string.Empty;
        public string primaryImage { get; set; } = string.Empty;
        public string primaryImageSmall { get; set; } = string.Empty;
        public string department { get; set; } = string.Empty;
        public string artistDisplayName { get; set; } = string.Empty;
        public string artistBeginDate { get; set; } = string.Empty;
        public string artistEndDate { get; set; } = string.Empty;
    }
}