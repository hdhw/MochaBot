namespace MochaBot.Utils;

public static class Leveling
{
    public static long XpForLevel(int level)
    {
        long total = 0;
        for (var n = 0; n < level; n++)
            total += 5 * ((long)n * n) + 50 * n + 100;
        return total;
    }

    public static int LevelForXp(long xp)
    {
        var level = 0;
        while (XpForLevel(level + 1) <= xp)
            level++;
        return level;
    }

    public static long XpIntoLevel(long xp)
    {
        var level = LevelForXp(xp);
        return xp - XpForLevel(level);
    }

    public static long XpToNext(long xp)
    {
        var level = LevelForXp(xp);
        return XpForLevel(level + 1) - XpForLevel(level);
    }
}
