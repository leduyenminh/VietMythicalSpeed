namespace VMS.Rendering
{
    /// <summary>
    /// Everything the graphics tier changes. Low targets older/budget phones (fill-rate bound), High targets
    /// recent flagships. The look stays the same; only resolution, post effects and scenery density scale.
    /// Ultra Low keeps what makes a race readable and fun (road, beat lines, pearls, vessel, exhaust, a few
    /// music bars, the theme's silhouettes) and drops the rest: decorative glows, far landmarks, most stars.
    /// </summary>
    public struct QualityProfile
    {
        public float renderScale;
        public int msaa;
        public bool bloom;
        public float propDensity;
        public int barsPerSide;
        public bool dragon;
        public int dragonSegments;
        public float drawAhead;
        public float fogEnd;
        public int maxSparks;
        /// <summary>Skip glow-only decorations and far landmarks; thin out the stars.</summary>
        public bool minimal;
        public float starScale;

        public static QualityProfile For(GraphicsTier tier)
        {
            switch (tier)
            {
                case GraphicsTier.UltraLow:
                    return new QualityProfile
                    {
                        renderScale = 0.6f, msaa = 1, bloom = false, propDensity = 0.3f, barsPerSide = 8,
                        dragon = false, dragonSegments = 0, drawAhead = 5f, fogEnd = 170f, maxSparks = 48,
                        minimal = true, starScale = 0.35f,
                    };
                case GraphicsTier.Low:
                    return new QualityProfile
                    {
                        renderScale = 0.75f, msaa = 1, bloom = false, propDensity = 0.5f, barsPerSide = 14,
                        dragon = false, dragonSegments = 0, drawAhead = 5.5f, fogEnd = 210f, maxSparks = 160,
                        starScale = 0.8f,
                    };
                case GraphicsTier.High:
                    return new QualityProfile
                    {
                        renderScale = 1f, msaa = 4, bloom = true, propDensity = 1f, barsPerSide = 24,
                        dragon = true, dragonSegments = 44, drawAhead = 7.5f, fogEnd = 300f, maxSparks = 512,
                        starScale = 1f,
                    };
                default:
                    return new QualityProfile
                    {
                        renderScale = 0.9f, msaa = 2, bloom = true, propDensity = 0.8f, barsPerSide = 20,
                        dragon = true, dragonSegments = 30, drawAhead = 6.5f, fogEnd = 260f, maxSparks = 320,
                        starScale = 1f,
                    };
            }
        }
    }
}
