namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE. World rules the server sends with the lights (0.13.2), as one int after the
    /// held-colour block, so a game older than 0.13.2 never reaches it and a server older than
    /// 0.13.2 simply sends none.
    ///
    /// Bit 0 set means sign colours are OFF on this world (`SignColours = false`): a cairn takes no
    /// colour from its sign's tag or `[bracket]`, only from what it holds. Stored as an OFF bit on
    /// purpose, so "no rules sent" and "zero" both mean the old behaviour: sign colours on.
    /// </summary>
    public static class ServerRules
    {
        private const int SignColoursOff = 1;

        public static int Encode(bool signColours) => signColours ? 0 : SignColoursOff;

        public static bool SignColours(int rules) => (rules & SignColoursOff) == 0;
    }
}
