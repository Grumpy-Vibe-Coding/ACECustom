namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// A zone's COSMETIC override set — kept deliberately separate from the stat/prop profile so that
    /// appearance is orthogonal to a monster's "true" stats and abilities. Every field is nullable: null
    /// means "not overridden, inherit". A zone carries one <see cref="ControlledArea.AppearanceDefault"/>
    /// plus per-WCID entries in <see cref="ControlledArea.AppearanceByWcid"/>; resolution LAYERS the two
    /// (per-WCID non-null wins over the default), unlike the stat profile which REPLACES. Because this lives
    /// outside <see cref="ACE.Server.Managers.ZoneScaling.ZoneVariantProfile"/>, setting a per-WCID
    /// appearance never creates a stat override bucket, so it can't detach a monster from zone stat scaling.
    ///
    /// Recolor/resize levers stamp through per-instance property writes at (re)spawn. The DataId levers
    /// (model/clothing/palette-base/tables) swap the actual model data — validated by DID class byte and, on
    /// a full model swap, applied after clearing the base weenie's own overlay so it doesn't bleed through.
    /// </summary>
    public class ZoneAppearance
    {
        // ── Recolor / resize (per-instance property writes) ──
        /// <summary>PropertyInt.PaletteTemplate (3): recolor via a ClothingBase's sub-palette option.</summary>
        public int? PaletteTemplate { get; set; }

        /// <summary>PropertyFloat.Shade (12): palette shade 0..1 (needs a ClothingBase to take).</summary>
        public double? Shade { get; set; }

        /// <summary>PropertyFloat.DefaultScale (39) = ObjScale: model size (1.0 = normal).</summary>
        public double? Scale { get; set; }

        /// <summary>PropertyFloat.Translucency (76): 0 = solid .. 1 = invisible.</summary>
        public double? Translucency { get; set; }

        /// <summary>PropertyInt.CreatureVariant (9038) shiny: true stamps 1 (shiny texture swap), false stamps 0.</summary>
        public bool? Shiny { get; set; }

        // ── Model / data swaps (DataId; validated by class byte in the applier) ──
        /// <summary>PropertyDataId.Setup (1) 0x02: the base model (body parts). A model swap.</summary>
        public uint? SetupTableId { get; set; }

        /// <summary>PropertyDataId.MotionTable (2) 0x09: animations — swap alongside a new Setup or it won't animate.</summary>
        public uint? MotionTable { get; set; }

        /// <summary>PropertyDataId.SoundTable (3) 0x20: creature sounds.</summary>
        public uint? SoundTable { get; set; }

        /// <summary>PropertyDataId.PaletteBase (6) 0x04: whole-object base palette (recolors a bare model too).</summary>
        public uint? PaletteBase { get; set; }

        /// <summary>PropertyDataId.ClothingBase (7) 0x10: clothing/armor overlay (must match the Setup to render).</summary>
        public uint? ClothingBase { get; set; }

        /// <summary>PropertyDataId.Icon (8) 0x06: the examine/selection icon.</summary>
        public uint? Icon { get; set; }

        public bool IsEmpty =>
            !PaletteTemplate.HasValue && !Shade.HasValue && !Scale.HasValue &&
            !Translucency.HasValue && !Shiny.HasValue &&
            !SetupTableId.HasValue && !MotionTable.HasValue && !SoundTable.HasValue &&
            !PaletteBase.HasValue && !ClothingBase.HasValue && !Icon.HasValue;

        /// <summary>Reset every field to "not overridden" (used by clearappearance-all / the plugin's Revert all).</summary>
        public void Clear()
        {
            PaletteTemplate = null; Shade = null; Scale = null; Translucency = null; Shiny = null;
            SetupTableId = null; MotionTable = null; SoundTable = null;
            PaletteBase = null; ClothingBase = null; Icon = null;
        }

        public ZoneAppearance Clone() => new ZoneAppearance
        {
            PaletteTemplate = PaletteTemplate,
            Shade = Shade,
            Scale = Scale,
            Translucency = Translucency,
            Shiny = Shiny,
            SetupTableId = SetupTableId,
            MotionTable = MotionTable,
            SoundTable = SoundTable,
            PaletteBase = PaletteBase,
            ClothingBase = ClothingBase,
            Icon = Icon,
        };

        /// <summary>Returns a new set = this overlaid by <paramref name="overlay"/> (overlay's non-null fields win).
        /// Used to layer a per-WCID entry over the zone default. Null args are treated as empty.</summary>
        public static ZoneAppearance Merge(ZoneAppearance base_, ZoneAppearance overlay)
        {
            if (base_ == null && overlay == null) return null;
            var result = base_?.Clone() ?? new ZoneAppearance();
            if (overlay == null) return result;
            if (overlay.PaletteTemplate.HasValue) result.PaletteTemplate = overlay.PaletteTemplate;
            if (overlay.Shade.HasValue) result.Shade = overlay.Shade;
            if (overlay.Scale.HasValue) result.Scale = overlay.Scale;
            if (overlay.Translucency.HasValue) result.Translucency = overlay.Translucency;
            if (overlay.Shiny.HasValue) result.Shiny = overlay.Shiny;
            if (overlay.SetupTableId.HasValue) result.SetupTableId = overlay.SetupTableId;
            if (overlay.MotionTable.HasValue) result.MotionTable = overlay.MotionTable;
            if (overlay.SoundTable.HasValue) result.SoundTable = overlay.SoundTable;
            if (overlay.PaletteBase.HasValue) result.PaletteBase = overlay.PaletteBase;
            if (overlay.ClothingBase.HasValue) result.ClothingBase = overlay.ClothingBase;
            if (overlay.Icon.HasValue) result.Icon = overlay.Icon;
            return result;
        }
    }
}
