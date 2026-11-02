// Messages that the app shows to the user follow its UI language, which it passes in LOCAL_DICTATION_LANGUAGE on every worker start. Other languages get English.
internal static class Messages
{
    static readonly bool German = Environment.GetEnvironmentVariable("LOCAL_DICTATION_LANGUAGE") == "de";
    public static string T(string english, string german) => German ? german : english;
}
