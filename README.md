# Mirage

Mirage is a mod that gives any enemy the ability to mimic a player's voice (fully synced to all players).  
**This mod is required by the host and on all clients.** Clients that do not have the mod will run into desynchronization issues.

## Features (all features listed below are configurable)

- Every enemy can mimic the voice of any player currently in the game.
  - On spawn, each enemy chooses a player to always mimic the voice of.
  - Voices are synced to all players. This means everyone hears the same voice, the same words, at the same time.
  - Only masked enemies mimic voices by default, other enemies can be enabled via config.
- Masked enemies use the mimicking player's suit and cosmetics.
- Remove the mask from masked enemies.
- Remove the arms-out animation from masked enemies.
- Configuration is synced to all players (only the host's config is used).
- Adjustable spawn chance for masked enemies.
  - Calculates the spawn weights for each moon internally.
  - Default is set to spawn masked enemies at 2% for each moon.
  - Note: Setting the spawn-rate to 25% basically guarantees a spawn per round.

## I have a question. How can I ask for help?

If you have question, simply create a [new discussion](https://github.com/qwbarch/mirage/discussions) on GitHub.
I'll respond as soon as I'm available.

## I found a bug. Where can I report it?

For bugs, submit a [new issue](https://github.com/qwbarch/mirage/issues) on GitHub. Please mention the bug report is specifically for ``Lethal Company``.

## Frequently asked questions

#### Do I need Skinwalkers for this mod to work?

No, Mirage is a standalone mod. Installing both Mirage and Skinwalkers will result in Skinwalkers playing voice at the same time as Mirage.

#### Can I use MaskedEnemyOverhaul with this mod?

MaskedEnemyOverhaul will cause the masked enemy's suit and mimicking voice to not match.  
Use [MaskedEnemyOverhaulFork](https://thunderstore.io/c/lethal-company/p/Coppertiel/MaskedEnemyOverhaulFork/) instead, with
the ``Dont Touch MaskedPlayerEnemy.mimickingPlayer`` configuration set to ``true``.

Unless you use the nameplate, fading mask, or zombie apocalypse feature(s), you probably don't need it though, since Mirage covers the rest of the features already.

#### Do I need DissonanceLagFix installed?

No. Mirage already applies the lag fix patch, since ``v1.0.16``.

#### Does this mod support cosmetics?

Yes, any mod that applies to masked enemies is compatible with Mirage.  
If the cosmetic mod you use does not support masked enemies, you will need to request the mod author to support it.

#### Does this mod use voice recognition and/or AI?

No. This was originally planned for the ``v2.0.0`` update, but the update has been dropped due to time constraints and life priorities.

#### Can I hear my own voice from voice mimics?

By default, yes. Each player can selectively choose to not hear monsters mimicking their voice by accessing the ``LethalSettings`` mod settings for ``Mirage``.

#### What happened to the v2 (voice recognition + machine learning) update?

While 99% of the work has been done, [1DWalker](https://github.com/1DWalker) and I have other life responsibilities to prioritize. We can no longer justify spending all of our time
working on an unpaid passion project, and as such, have decided to drop the update altogether.  
To anyone who was excited for the v2 update: I apologize for the disappointment, and I also thank you for all the support.

## Recommended mods

- [StarlancerAIFix](https://thunderstore.io/c/lethal-company/p/AudioKnight/StarlancerAIFix/) - Fixes the ``EnableEnemyMesh`` spam when masked enemies move to areas it shouldn't have access to.
- [GeneralImprovements](https://thunderstore.io/c/lethal-company/p/ShaosilGaming/GeneralImprovements/) - Optionally adds player nametags to masked enemies, disables radar spinning, etc.
- [Zombies](https://thunderstore.io/c/lethal-company/p/Synaxin/Zombies/) - ``SpawnOnPlayerDeath`` has been removed, and this mod covers that functionality, plus more! Use this if you want players to revive as a zombie.
- [LethalIntelligenceExperimental](https://thunderstore.io/c/lethal-company/p/VirusTLNR/LethalIntelligenceExperimental/) - Masked AI changes, as well as supporting walkie talkies with Mirage.

## Can I reupload the mod to Thunderstore?

No, reuploading the mod to Thunderstore is not permitted. If you are creating a modpack, please use the official mod.  
If you're making small changes for your friends, you will need to share the compiled ``.dll`` directly with them, and then import it locally.

## Credits

- [1DWalker](https://github.com/1DWalker) - Huge thanks for all the hard work put towards the v2 update. While the v2 update will no longer be released, I still want to point out that the update would not have even been possible without his work towards the machine-learning part of the codebase.  
- [IntegrityFate](https://integrityfate.com/) - Commissioned to create the new Mirage logo, putting his own spin on the design. You can find him here: https://integrityfate.com/

## Acknowledgements

- [1DWalker](https://github.com/1DWalker) - Huge contributor to the dropped v2 update, and is the main person I ran tests with throughout the entire development of Mirage.
- [RugbugRedfern](https://rugbug.net) - Mirage is heavily inspired by [Skinwalkers](https://thunderstore.io/c/lethal-company/p/RugbugRedfern/Skinwalkers/). Thank you for creating one of the best mods in the game!
- [Evaisa](https://github.com/EvaisaDev) and [LordFireSpeed](https://github.com/Lordfirespeed) - For creating the amazing [UnityNetcodePatcher](https://github.com/EvaisaDev/UnityNetcodePatcher), which this mod uses during its build process.
- [Lunxara](https://www.twitch.tv/lunxara) - Helped host a lot of experimental tests for Mirage. Check out her stream! https://www.twitch.tv/lunxara
- [TheDebbyCase](https://thunderstore.io/c/lethal-company/p/deB) - For the countless days spent helping me reproduce issues that other players were having, as well as explaining how spawn curves work.
- [Owen3H](https://github.com/Owen3H) - For his synced configuration [implementation](https://gist.github.com/Owen3H/c73e09314ed71b254256cbb15fd8c51e/5f314116ccd2ba3e5a2a38f01cf889dc674f2cfa), as well as bringing up issues with the approach taken from the modding wiki.
- [MartinEvans](https://github.com/martindevans) - Author of [dissonance](https://placeholder-software.co.uk/dissonance/docs/index.html), for helping me out with voice activity related issues.
- [IAmBatby](https://github.com/IAmBatby) and [BananaPuncher714](https://github.com/BananaPuncher714) - For answering my spawn control related questions, regarding [LethalLevelLoader](https://thunderstore.io/c/lethal-company/p/IAmBatby/LethalLevelLoader/) and [LethalQuantities](https://thunderstore.io/c/lethal-company/p/BananaPuncher714/LethalQuantities/).
- [Zaggy1024](https://github.com/Zaggy1024) - For pointing me towards setting up a debug build of the game, which is required for me to be able to run a performance profiler.
- [IAmBatby](https://github.com/IAmBatby) and [mattymatty](https://github.com/mattymatty97) - For their help/implementation on getting rid of the audio spatializer warning log spam.
- [Lunxara](https://www.twitch.tv/lunxara), [JacuJ](https://thunderstore.io/c/lethal-company/p/JacuJ/), BBAPepsiMan, and [JokerMan](https://steamcommunity.com/id/t1gre456/) - For testing the final experimental update. I was only able to fix a lot of the subtle bugs they found because of their extensive testing.
- Endoxicom - Helped test a lot of the v2 update's hardware requirements that I was trying to figure out at the time.
- [Vladiester](https://www.youtube.com/@Vladiester) - Helped test a lot of the experimental builds, as well as testing my Content Warning version of the mod.

## Changelog

To stay up to date with the latest changes, click [here](https://thunderstore.io/c/lethal-company/p/qwbarch/Mirage/changelog) to view the changelog.
