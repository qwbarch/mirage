# Mirage

Mirage is a mod that gives any enemy the ability to mimic a player's voice (fully synced to all players).  
**This mod is required by the host and on all clients.** Clients that do not have the mod will run into desynchronization issues.

## Features

- Mimic the voice of a player for any enemy (such as bracken, dress girl, modded enemies, etc)
   - Use the same player's voice every time it attempts to mimic their voice
   - Voice is synced to all players, where everyone hears the same voice
   - Only masked enemies mimic voices by default, other enemies can be enabled via config
- Spawn a masked enemy on player death (like a player turning into a zombie)
   - Chance to spawn on death can be configured
   - Set this to 0 to disable the feature
   - Can be configured to only spawn if the dying player is alone
- Masked enemies use the mimicking player's suit
- Remove the mask off of masked enemy
- Remove the arms out animation off of masked enemy
- Configuration is synced to all players (only the host's config is used)
- Adjustable spawn chance for masked enemies
   - Calculates the spawn weights for each moon internally
   - Default is set to spawn masked enemies at 15% for each moon

## Discord

If you have questions, and/or want to stay up-to-date with the mod:

1. Join the lethal company modding [discord](https://discord.gg/lcmod).
2. Go to the mirage [release thread](https://discord.com/channels/1168655651455639582/1200695291972685926) and ask your question!
3. Optional: If you'd like to see a sneakpeek on what's potentially coming in v2.0.0, click [here](https://discord.com/channels/1168655651455639582/1200695291972685926/1210038530160599060).

##  I have a suggestion for the mod, and/or have found a bug

Whether you have a suggestion or have a bug to report, please submit it as an issue [here](https://github.com/qwbarch/lc-mirage/issues/new).

## Frequently asked questions

#### Do I need Skinwalkers for this mod to work?

No, Mirage is a standalone mod. Installing both Mirage and Skinwalkers will result in some voice clips to be unsynced.

#### Can I use MaskedEnemyOverhaul with this mod?

MaskedEnemyOverhaul will cause the masked enemy's suit and mimicking voice to not match.  
Use [MaskedEnemyOverhaulFork](https://thunderstore.io/c/lethal-company/p/Coppertiel/MaskedEnemyOverhaulFork/) instead, with
the ``Dont Touch MaskedPlayerEnemy.mimickingPlayer`` configuration set to ``true``.

Unless you use the nameplate, fading mask, or zombie apocalypse feature(s), you probably don't need it though, since Mirage covers the rest of the features already.

#### Do I need DissonanceLagFix installed?

No. Mirage now applies the lag fix patch as of ``v1.0.16``.

#### Does this mod support cosmetics?

Yes, any mod that applies to masked enemies should be compatible with Mirage.  
If the cosmetic mod you use does not support masked enemies, you will need to request the mod author to support it.

#### Does this mod use voice recognition and/or AI?

Not currently, but it is currently a work in progress and will eventually come in ``v2.0.0``.

#### Can I hear my own voice from voice mimics?

By default, yes. You can configure to not be able to hear them while alive, and resume being able to hear them while spectating.  

#### Is using Mirage to override the masked enemy's spawn rate compatible with spawn control mods?

While only [LethalQuantities](https://thunderstore.io/c/lethal-company/p/BananaPuncher714/LethalQuantities/) and [LethalLevelLoader](https://thunderstore.io/c/lethal-company/p/IAmBatby/LethalLevelLoader/) has been tested, Mirage ***will*** override the spawn weights for masked enemies, since its patches run after them.

This means only masked enemy's spawn weights will be replaced with what Mirage calculates (based on the percentage you desire), and the rest of the enemies
will remain untouched.

## Recommended mods

- [StarlancerAIFix](https://thunderstore.io/c/lethal-company/p/AudioKnight/StarlancerAIFix/) - Fixes a vanilla error referencing ``EnableEnemyMesh``.
- [LCMaskedFix](https://thunderstore.io/c/lethal-company/p/kuba6000/LC_Masked_Fix/) - Fixes vanilla issues with masked enemies.
- [EnemyFix](https://thunderstore.io/c/lethal-company/p/SZAKI/EnemyFix/) - Failsafe for when mod conflicts occur, enemies will still gracefully despawn.
- [GeneralImprovements](https://thunderstore.io/c/lethal-company/p/ShaosilGaming/GeneralImprovements/) - Quality of life, as well as the option to disable player name tags (to make it harder to spot masked enemies).
- [AsyncLoggers](https://thunderstore.io/c/lethal-company/p/mattymatty/AsyncLoggers/) - Increases performance by making logs write to another thread. Mirage already
   writes logs to a separate thread, but this is still good to have for other mods that you have.

## Can I reupload the mod to Thunderstore?

No, reuploading the mod to Thunderstore is not permitted. If you are creating a modpack, please use the official mod.  
If you're making small changes for your friends, you will need to share the compiled ``.dll`` directly with them, and then import it locally.

## Acknowledgements

- [RugbugRedfern](https://rugbug.net) - Mirage is heavily inspired by [Skinwalkers](https://thunderstore.io/c/lethal-company/p/RugbugRedfern/Skinwalkers/). Thank you for creating one of the best mods in the game!
- [Evaisa](https://github.com/EvaisaDev) and [LordFireSpeed](https://github.com/Lordfirespeed) - For creating the amazing [UnityNetcodePatcher](https://github.com/EvaisaDev/UnityNetcodePatcher), which this mod uses during its build process.
- [Owen3H](https://github.com/Owen3H) - For their synced configuration [implementation](https://gist.github.com/Owen3H/c73e09314ed71b254256cbb15fd8c51e/5f314116ccd2ba3e5a2a38f01cf889dc674f2cfa), as well as bringing up issues with the approach taken from the modding wiki.
- [MartinEvans](https://github.com/martindevans) - Author of [dissonance](https://placeholder-software.co.uk/dissonance/docs/index.html), for helping me out with voice activity related issues.
- [IAmBatby](https://github.com/IAmBatby) and [BananaPuncher714](https://github.com/BananaPuncher714) - For answering my spawn control related questions, regarding [LethalLevelLoader](https://thunderstore.io/c/lethal-company/p/IAmBatby/LethalLevelLoader/) and [LethalQuantities](https://thunderstore.io/c/lethal-company/p/BananaPuncher714/LethalQuantities/).

## Changelog

To stay up to date with the latest changes, click [here](https://thunderstore.io/c/lethal-company/p/qwbarch/Mirage/changelog) to view the changelog.
