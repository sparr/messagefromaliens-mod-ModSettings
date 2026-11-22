namespace ModSettings
{
    // Members other mods can call, by reflection when they do not reference this assembly. Their
    // names and signatures stay the same across versions.
    public static class Api
    {
        // Whether the glyph is a choice on the Mod Settings page that is laid out and showing:
        // one of the options after a setting's name, or one on a page of a setting's choices.
        // False for every other glyph, including setting names, lines that open a page, and
        // glyphs of pages no longer showing.
        public static bool IsChoiceGlyph(AlienCharacter glyph) => SettingsPages.IsChoice(glyph);
    }
}
