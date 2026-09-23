namespace CinderPass.Core
{
    public enum GameState
    {
        Boot,
        /// <summary>Spline autopilot drives; player input disabled.</summary>
        Intro,
        /// <summary>Player drives with full physics.</summary>
        Playing,
        /// <summary>Vehicle entered a hazard; physics frozen while the effect plays.</summary>
        Hazard,
        /// <summary>Screen faded, vehicle being restored to a safe route position.</summary>
        Respawning
    }
}
