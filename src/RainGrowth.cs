using System.Collections.Generic;

namespace KitsuneBetterFarming
{
    /// <summary>
    /// Rain makes plants grow a little faster.
    ///
    /// A growing plant has one scheduled block tick per growth stage (see
    /// BlockPlantGrowing.addScheduledTick). Every few seconds, while it rains in
    /// a plant's biome and the plant is under open sky, we pull that tick
    /// forward by Boost x the time that just passed. So a plant that sits in
    /// rain the whole stage finishes it (1 + Boost) times as fast, and one that
    /// catches a short shower gains only that shower's worth.
    ///
    /// Server side only, loaded chunks only (the ticker only holds those).
    /// Snow does not count. Applies to anything that grows in stages, so
    /// planted trees benefit too.
    /// </summary>
    public class RainGrowthMod : IModApi
    {
        /// <summary>Extra growth while it rains: 0.25 = 25% faster.</summary>
        public const float Boost = 0.25f;

        /// <summary>Game ticks between sweeps (20 ticks = 1 second).</summary>
        private const ulong SweepTicks = 200;

        /// <summary>Rain strength (0 to 1) below which it does not count as raining.</summary>
        private const float MinRain = 0.05f;

        private static ulong lastSweep;
        private static readonly List<WorldBlockTickerEntry> scratch = new List<WorldBlockTickerEntry>();

        public void InitMod(Mod _modInstance)
        {
            ModEvents.GameUpdate.RegisterHandler(OnGameUpdate);
            Log.Out("[KitsuneBetterFarming] Rain growth: plants under open sky grow {0}% faster while it rains", (int)(Boost * 100f));
        }

        private static void OnGameUpdate(ref ModEvents.SGameUpdateData _data)
        {
            if (GameTimer.Instance == null)
            {
                return;
            }
            ulong now = GameTimer.Instance.ticks;
            if (now < lastSweep)
            {
                lastSweep = now; // a different world was loaded
            }
            ulong elapsed = now - lastSweep;
            if (elapsed < SweepTicks)
            {
                return;
            }
            lastSweep = now;
            if (elapsed > SweepTicks * 4)
            {
                return; // first sweep after loading, or a long hitch: nothing to credit
            }
            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || world.IsRemote() || WeatherManager.Instance == null)
            {
                return;
            }
            if (!AnyRain())
            {
                return;
            }
            WorldBlockTicker ticker = world.GetWBT();
            if (ticker == null)
            {
                return;
            }
            ulong shave = (ulong)(elapsed * Boost);
            if (shave == 0)
            {
                return;
            }
            lock (ticker.lockObject)
            {
                scratch.Clear();
                foreach (WorldBlockTickerEntry entry in ticker.scheduledTicksDict.Values)
                {
                    // Needs more than this sweep's worth left, so we never fire a tick early by more than we earned.
                    if (entry.scheduledTime > now + shave)
                    {
                        scratch.Add(entry);
                    }
                }
                for (int i = 0; i < scratch.Count; i++)
                {
                    WorldBlockTickerEntry entry = scratch[i];
                    Vector3i pos = entry.worldPos;
                    BlockValue bv = world.GetBlock(pos);
                    if (bv.type != entry.blockID || !(bv.Block is BlockPlantGrowing))
                    {
                        continue;
                    }
                    if (RainAt(world, pos) < MinRain || !world.IsOpenSkyAbove(pos.x, pos.y, pos.z))
                    {
                        continue;
                    }
                    // Replaces the plant's existing entry with an earlier one.
                    ticker.AddScheduledBlockUpdate(pos, entry.blockID, entry.scheduledTime - now - shave);
                }
                scratch.Clear();
            }
        }

        private static bool AnyRain()
        {
            if (WeatherManager.forceRain >= 0f)
            {
                return WeatherManager.forceRain >= MinRain;
            }
            List<WeatherManager.BiomeWeather> all = WeatherManager.Instance.biomeWeather;
            if (all == null)
            {
                return false;
            }
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].rainParam.value >= MinRain)
                {
                    return true;
                }
            }
            return false;
        }

        private static float RainAt(World world, Vector3i pos)
        {
            if (WeatherManager.forceRain >= 0f)
            {
                return WeatherManager.forceRain;
            }
            BiomeDefinition biome = world.GetBiome(pos.x, pos.z);
            if (biome == null)
            {
                return 0f;
            }
            WeatherManager.BiomeWeather bw = WeatherManager.Instance.FindBiomeWeather(biome.m_BiomeType);
            return bw != null ? bw.rainParam.value : 0f;
        }
    }
}
