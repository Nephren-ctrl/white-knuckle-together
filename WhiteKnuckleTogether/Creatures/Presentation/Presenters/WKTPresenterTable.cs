using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which creature types get which presenter, from what their own ticks show (see each presenter). Subtypes are listed
    /// before their base types; a type not listed uses its nearest listed base type, if any.
    /// </summary>
    internal static class WKTPresenterTable {
        public static readonly (Type EntityType, Func<GameEntity, WKTPuppetPresenterBase> Create)[] Factories = {
            (typeof(DEN_Face), entity => new WKTFacePresenter(entity)),
            (typeof(DEN_Teeth), entity => new WKTTeethPresenter(entity)),
            (typeof(DEN_Hunter), entity => new WKTHunterPresenter(entity)),
            (typeof(DEN_VentThing), entity => new WKTVentThingPresenter(entity)),
            (typeof(DEN_RatKing), entity => new WKTRatKingPresenter(entity)),
            (typeof(DEN_Turret), entity => new WKTTurretPresenter(entity)),
            (typeof(DEN_Gasbag), entity => new WKTGasbagPresenter(entity)),
            (typeof(DEN_Hopper_Tick), entity => new WKTTickPresenter(entity)),
            (typeof(DEN_BasicDenizen_Flier_Swarm), entity => new WKTSwarmPresenter(entity)),
            (typeof(DEN_BasicDenizen_Flier), entity => new WKTFlierPresenter(entity)),
            (typeof(DEN_Drone), entity => new WKTFlierPresenter(entity)),
            (typeof(DEN_Bloodbug), entity => new WKTFlierPresenter(entity)),
            (typeof(DEN_SlugGrub), entity => new WKTSlugGrubPresenter(entity)),
            (typeof(DEN_GantryTurret), entity => new WKTGantryTurretPresenter(entity)),
            // Any other basic denizen aiming with turret segments, such as the Ravelin.
            (typeof(DEN_BasicDenizen), entity => new WKTTurretSegmentsPresenter(entity))
        };
    }
}
