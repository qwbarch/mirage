# Mirage

Mirage is a mod that gives any enemy the ability to mimic a player's voice (fully synced to all players). **This mod is required by the host and on all clients.**  

> ⚠️**Warning: This mod is not a drop-in replacement for Skinwalkers + MaskedEnemyOverhaul.**  
> This mod comes with features you might not want by default (such as masked enemies spawning on every moon), and will require you to [tweak the configuration](https://github.com/qwbarch/mirage/tree/production#why-does-this-mod-come-with-so-many-features-i-only-want-skinwalkers-but-with-synced-voices).

<a href="https://youtu.be/yThkkWAOc6Q">
  <img src="https://markdown-videos-api.jorgenkh.no/url?url=https%3A%2F%2Fyoutu.be%2FyThkkWAOc6Q" alt="Mod showcase" title="Mod showcase" width="500vw"/>
</a>

## Features (all features listed below are configurable)

- Every enemy can mimic the voice of any player currently in the game.
  - On spawn, each enemy chooses a player to always mimic the voice of.
  - Voices are synced to all players. This means everyone hears the same voice, the same words, at the same time.
  - Only masked enemies mimic voices by default, other enemies can be enabled via config.
-  Spawns a masked enemy on player death, at player's death location (like a player turning into a zombie).
  - Default is set to a 10% chance to spawn a masked enemy, set this to 0 to disable the feature.
  - Can be configured to only spawn if the dying player is alone.
  - Can spawn at the company building if [NavMeshInCompany](https://thunderstore.io/c/lethal-company/p/Kittenji/NavMeshInCompany/) is installed.
- Masked enemies use the mimicking player's suit and cosmetics.
- Remove the mask from masked enemies.
- Remove the arms-out animation from masked enemies.
- Configuration is synced to all players (only the host's config is used).
- Adjustable spawn chance for masked enemies.
  - Calculates the spawn weights for each moon internally.
  - Default is set to spawn masked enemies at 15% for each moon.

## Discord

If you have questions, and/or want to stay up-to-date with the mod:

1. Join the lethal company modding [discord](https://discord.gg/lcmod).
2. Go to the mirage [release thread](https://discord.com/channels/1168655651455639582/1200695291972685926) and ask your question!
3. Optional: If you'd like to see a sneakpeek on what's potentially coming in v2.0.0, click [here](https://discord.com/channels/1168655651455639582/1200695291972685926/1210038530160599060).

##  I have a suggestion for the mod, and/or have found a bug

Whether you have a suggestion or have a bug to report, please submit it as an issue [here](https://github.com/qwbarch/lc-mirage/issues/new).

## Frequently asked questions

#### Why does this mod come with so many features? I only want skinwalkers, but with synced voices.

Mirage is not Skinwalkers, despite the overlap in many features. The main goal of this mod is to overhaul the masked enemy experience.  
If you want an experience more similar to ``Skinwalkers`` while keeping everything else vanilla, you will need to modify these config values:
- ``EnableOverrideSpawnChance = false``
- ``SpawnOnPlayerDeath = 0``
- ``EnableMask = true``
- ``EnableArmsOut = true``

Keep in mind only masked enemies mimic voices by default, so you will need to enable other enemies if you want them to mimic voices.

#### Can I use MaskedEnemyOverhaul with this mod?

You probably don't need MaskedEnemyOverhaul when using Mirage, since this mod already supports:
- Removal of the mask texture and arms-out animation.
- Naturally spawned masked enemies mimic a random player.
- Cosmetics are supported (by the cosmetics mods themselves).
- Spawn control for masked enemies is configurable.

If you still want to use it anyways, make sure you use [MaskedEnemyOverhaulFork](https://thunderstore.io/c/lethal-company/p/Coppertiel/MaskedEnemyOverhaulFork/) instead,
with the ``Dont Touch MaskedPlayerEnemy.mimickingPlayer`` configuration set to ``true``.  
The original MaskedEnemyOverhaul will cause the masked enemy's suit and mimicking voice to not match.  

#### Do I need DissonanceLagFix installed?

No. Mirage now applies the lag fix patch as of ``v1.0.16``.

#### Does this mod support cosmetics?

Yes, any mod that applies to masked enemies should be compatible with Mirage.  
If the cosmetic mod you use does not support masked enemies, you will need to request the mod author to support it.

#### Does this mod use voice recognition and/or AI?

Not currently, but it is currently a work in progress and will eventually come in ``v2.0.0``.

#### Can I hear my own voice from voice mimics?

By default, yes. If you don't want to hear your own voice while you're alive, set ``MuteLocalPlayerVoice`` to ``true``.

#### Is using Mirage to override the masked enemy's spawn rate compatible with spawn control mods?

While only [LethalQuantities](https://thunderstore.io/c/lethal-company/p/BananaPuncher714/LethalQuantities/) and [LethalLevelLoader](https://thunderstore.io/c/lethal-company/p/IAmBatby/LethalLevelLoader/) has been tested, Mirage ***will*** override the spawn weights for masked enemies, since its patches run after them.

This means only masked enemy's spawn weights will be replaced with what Mirage calculates (based on the percentage you desire), and the rest of the enemies
will remain untouched.

#### Why does SpawnOnPlayerDeath not work at the company building?

You need to install [NavMeshInCompany](https://thunderstore.io/c/lethal-company/p/Kittenji/NavMeshInCompany/) to allow enemies to exist at the company building.

## Recommended mods

- [StarlancerAIFix](https://thunderstore.io/c/lethal-company/p/AudioKnight/StarlancerAIFix/) - Fixes a vanilla error referencing ``EnableEnemyMesh``.
- [LCMaskedFix](https://thunderstore.io/c/lethal-company/p/kuba6000/LC_Masked_Fix/) - Fixes vanilla issues with masked enemies.
- [EnemyFix](https://thunderstore.io/c/lethal-company/p/SZAKI/EnemyFix/) - Failsafe for when mod conflicts occur, enemies will still gracefully despawn.
- [GeneralImprovements](https://thunderstore.io/c/lethal-company/p/ShaosilGaming/GeneralImprovements/) - Quality of life, as well as the option to disable player name tags (to make it harder to spot masked enemies).
- [AsyncLoggers](https://thunderstore.io/c/lethal-company/p/mattymatty/AsyncLoggers/) - Increases performance by making logs write to another thread. Mirage already
   writes logs to a separate thread, but this is still good to have for other mods that you have.
- [DramaMask](https://thunderstore.io/c/lethal-company/p/necrowing/DramaMask/) - Allows you to hide from masked enemies, and if you have mask textures enabled, you can troll your friends by looking identical to a masked enemy.

## Can I reupload the mod to Thunderstore?

No, reuploading the mod to Thunderstore is not permitted. If you are creating a modpack, please use the official mod.  
If you're making small changes for your friends, you will need to share the compiled ``.dll`` directly with them, and then import it locally.

## Acknowledgements

- [RugbugRedfern](https://rugbug.net) - Mirage is heavily inspired by [Skinwalkers](https://thunderstore.io/c/lethal-company/p/RugbugRedfern/Skinwalkers/). Thank you for creating one of the best mods in the game!
- [Evaisa](https://github.com/EvaisaDev) and [LordFireSpeed](https://github.com/Lordfirespeed) - For creating the amazing [UnityNetcodePatcher](https://github.com/EvaisaDev/UnityNetcodePatcher), which this mod uses during its build process.
- [Owen3H](https://github.com/Owen3H) - For their synced configuration [implementation](https://gist.github.com/Owen3H/c73e09314ed71b254256cbb15fd8c51e/5f314116ccd2ba3e5a2a38f01cf889dc674f2cfa), as well as bringing up issues with the approach taken from the modding wiki.
- [MartinEvans](https://github.com/martindevans) - Author of [dissonance](https://placeholder-software.co.uk/dissonance/docs/index.html), for helping me out with voice activity related issues.
- [IAmBatby](https://github.com/IAmBatby) and [BananaPuncher714](https://github.com/BananaPuncher714) - For answering my spawn control related questions, regarding [LethalLevelLoader](https://thunderstore.io/c/lethal-company/p/IAmBatby/LethalLevelLoader/) and [LethalQuantities](https://thunderstore.io/c/lethal-company/p/BananaPuncher714/LethalQuantities/).
- [TheDebbyCase](https://thunderstore.io/c/lethal-company/p/deB) - For the countless days spent helping me reproduce issues that other players were having, as well as explaining how spawn curves work.
- [Zaggy1024](https://github.com/Zaggy1024) - For pointing me towards setting up a debug build of the game, which is required for me to be able to run a performance profiler.
- [IAmBatby](https://github.com/IAmBatby) and [mattymatty](https://github.com/mattymatty97) - For their help/implementation on getting rid of the audio spatializer warning log spam.

## Changelog

To stay up to date with the latest changes, click [here](https://thunderstore.io/c/lethal-company/p/qwbarch/Mirage/changelog) to view the changelog.
