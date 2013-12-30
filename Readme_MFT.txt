***Modular Fuel Tanks***
by taniwha, based on work by NathanKell and ChestBurster for Modular Fuel Systems Continued.

ialdabaoth (who is awesome) created Modular Fuels, and we're maintaining it in his absence.

License remains CC-BY-SA as modified by ialdabaoth.

Also included: Module Manager (by sarbian, based on ialdabaoth's work). See Module Manager thread for details and license and source: http://http://forum.kerbalspaceprogram.com/threads/55219
Module Manager is required for MFSC to work.

Mods Supported, beyond Stock KSP:
AIES
B9
FASA (in progress)
HexCans
Kethane
KOSMOS
KSPX
KW Rocketry
NovaPunch
RLA Stockalikes
Space Shuttle Engines
SDHI Service Module
Spherical Tanks
Taverio's Pizza and Aerospace (TVPP)
THHS
Touhou Torpedo's Mk3 and Mk4 mods

INSTALL INSTRUCTIONS:
1. Delete any existing ModularFuelTanks or RealFuels folder in your KSP/GameData folder. This is VITAL.
2. Extract this archive to your KSP/GameData folder

AN OVERVIEW OF FUEL TYPES AND TECH LEVELS ARE BELOW THE CHANGELOG

==========
Changelog:
v4.0 = \/
*0.23 compatibility rewrite by taniwha
*Numerous bugfixes
*Completely separate from RealFuels.
*Utilization fixed: now it determines under what pressure are the contents of the tank.

v3.4 = \/ (unreleased)
*Fixed basemass issue when using custom basemass (Thanks, RedAV8R!)
*Fixed Hybrid Engine techlevel support
*Native KSPI support

v3.3 = \/
*Swapped how thrust and mass are changed by TL increase.
*Made the battery multiplier (how much charge per unit of volume) configurable in MFSSettings
*Changed Xenon around. Tanks now hold 1/10 what they used to, and Xenon is now 10x as dense. It's kept pressurized at a hopefully reasonable temperature to yield that 0.2 g/cc density (what I've set it to).
*Changed tank masses again to try go get them ever closer in line with the real world.
*Added Balloon tanktype (practically no basemass; structural integrity kept by internal pressure). C.f. Atlas missile / LV. Same for BalloonCryo. Since the tank goes all the way to the skin they hold slightly more (at least the StretchyTank ones, the only ones so far, do).
*Fixed propellant ratios so that they are displayed in percents rather than 0.x ratios that get rounded.
*Added tooltips for hovering over autofill buttons to say which engines use that mixture.
*Added SRFirefox's fix for NTRs so nuclear fuel lasts longer. Also increased ElectricCharge generation.
*Added Syntin; changed Methane to LqdMethane to comport with other fuels.

v3.2 = \/
*Typo fix (thanks Starwaster!) to [un]fillable tanks.
*Increased precision for fuel densities; fixed LH2's density (was 0.071g/cc; should be 0.07085g/cc)
*Added tank support for Space Shuttle Engines mod (thanks, Malsheck!)
*Added scrolling to tank configuration.
*Upgraded to Module Manager v1.5 

v3.1 = \/
*Changed zip's path structure.
*Redid StretchyTanks support again. Get StretchySRB v5.
*Added new engines and tanks thanks to Chestburster's hard work
 -Added missing tanks from FASA
 -Added support for SDHI Service Module (take some Oxidizer and Liquid Fuel with you for the built-in FuelCell)
 -Added support for "new" Spherical Tanks
 -Added dtobi's Space Shuttle Engines
*Internal tweaks
*Added localCorrectThrust, in the ModuleEngineConfigs module. Set to false if MFS should not alter thrust due to Isp change (like for jets).
*Added useConfigAsTitle. Will change part title to config name when setting configuration, if true.
*Added new resources: UDMH (same as monopropellant but set to STACK_PRIORITY_SEARCH not ALL_VESSEL), Hydrazine, Aerozine, and Methane.

v3 === \/
*Finally put back stock masses and thrusts (well, in 99.9% of cases) for all engines. Some had to be tweaked ever-so-slightly as they were balance-breakers.
*Changed the Isp and TWR curves slightly for the tech levels
*added heatMultiplier setting to RealSettings.cfg. Change it to change the global heat multiplier for all ModularEngines.
*Fixed more typos
*Added missing tanks
*Redid the tank masses again for Realistic Masses mode. It should roughly correspond to S-IC for kerolox, Shuttle External Tank for cryogenic hydrolox, and Titan II-1 for hypergolic. In each case a slight additional mass was taken into account to stand for additional stage mass like fairings and decouplers; if you have a fuel tank alone, you'll get a better fuel fraction than real life stages. Note that since engines masses in MFSC incorporate thrust plate mass, tank mass will also be lower.
*Added enhanced StretchyTanks compatibility. Things should scale better now. Requires newest version of NathanKell's StretchySRB patch (unzip on top of StretchyTanks).

v3 Alpha=== \/
*Fixed some typos.
*Fixed symmetry bug (thanks Starwaster!)
*Fixed heat fin shutdown gui (thanks Starwaster!)
*Added support for real mode, where tanks and engines have real TWRs. For now we are using some temporary engine configs, so your engine performance may have changed. If you want to enable real mode, after extracting the RealFuels zip per instructions, open the ModularFuelTanks/RealFuels/RealSettings.cfg file and change the line useRealisticMass = false to useRealisticMass = true
(To support this, tank masses are slightly imprecise.)

v2 === \/
*Fixed problems in KW engine configs
*Added dynamic changing of tech levels
*Tweaked B9 SABRE Isp

v1a === \/
*Fixed typo in Liquid H2 density
*Fixed B9 SABRE air-breathing fuel:oxidizer (aka air) ratio. Was 1:6, should be 1:23.
*Forgot to add a note to the readme regarding Thermal Fins.

v1 === \/
*Initial release.
**New features for standard Modular Fuel Tanks**
*New from ialdabaoth's dev build: thermal fins, for cooling your parts.

*Bugifx to tank code so no longer is amount ignored when maxAmount is set by percent. amount now acts as a ratio of maxAmount if maxAmount is set by percent.

*Special handling for ElectricCharge. 1 unit of volume = 100 units of charge.

*New tank type: ServiceModule (includes fuel, monopropellant, and electric charge).

*New options for basemass. If basemass is -1, then all MFT mass calculations regarding tank mass are ignored; the part's mass will be left unchanged.

*Tank option overriding. When you add a ModuleFuelTanks module to a part, you can specify a custom basemass, which will override the TANKTYPE basemass. In addition, you can add TANK nodes, just like you were in a TANKTYPE node, and override the TANKTYPE's values. For example, if you want only oxidizer in your special tank by default, but want to leave the option for more, you can add a MFT module with tank type = Default, but then add a TANK{} node:
TANK
{
	name = LiquidFuel
	amount = 0
	maxAmount = 0
}
and it will override tank type Default's value for liquid fuel. Note that as yet you can't add tanks of resource type unsupported by that tank type.

**New features for RealFuels / Modular Engines**
*A complete rebalancing of nearly all engines to use real-world fuels at realistic specific impulses. Engines are classified by tech level and by type, and use appropriate Isps at sea level and in vacuum.

*Tech level can be changed in engine config, just like fuel mode.

*New from ialdabaoth's dev build: Hybrid Engines now work just like regular Modular Engines (i.e. they have CONFIGs), it's just you can switch them on the fly.

*New from ialdabaoth's dev build: thrust now, like in real life, scales with Isp. Fuel flow is constant, but thrust changes with air pressure. For this reason, lower-stage engines have been given a boost in thrust proportional to what they lost. Note that MechJeb, Kerbal Engineer Redux, and other thrust-showing mods will only show current TWR, and the VAB/SPH is considered vacuum. MechJeb now supports showing Sea Level TWR when a Modular Engine is attached to the vessel, and correctly calculates stage time and TWR during flight. KER support forthcoming.

*You may now have both a Modular Tanks AND a Modular Engines module in the same part, and both will work in the VAB/SPH. The ME display will be shifted to the right.
=======================
ON Fuel Types:
*LiquidFuel (aka kerosene aka RP-1) and LOx is the "standard" / "benchmark" fuel. It's what the early launchers used, and what many still do. It's non-toxic, quite dense, and the LOx is only mildly cryogenic, which means LOx boiloff is not a big issue except for weeks+ missions. For all these reasons, RP-1/LOx is the fuel of choice for lower stages.

*Liquid H2 and LOx is a high-performance fuel. It gives you more "bang for your buck" but it's less dense so you need more tanks (though the tanks are lighter). However, engines burning LH2/LOx generally have a lower TWR than other engines (in MFS, 75%). Also, it is generally less efficient at sea level (proportionally) than other fuel mixes. The main complication however is that LH2 must be kept extremely cold; boiloff is a serious issue even in insulated (cryogenic) tanks. You should always use cryogenic tanks with this mixture; they mass less for hydrolox than do regular tanks. Given these advantages and disadvantages, LH2/LOx is best for upper stages that are fired during the launch or a few days after; however, some lower-stage use has been done (Delta IV, Ariane 5, Shuttle / Ares / SLS) though often in combination with solid boosters.

*Liquid Methane and LOx is midway between RP-1/LOx and LH2/LOx: lower performance than hydrolox but denser, and less cryogenic.

*Various hypergolics. They are various nitrogen-based storable (but highly toxic) liquid fuels. They perform less well than RP-1/LOx (about 95% the specific impulse), but are slightly denser and non-cryogenic: they can be stored for months at a time. Given their advantage in density, in actual use they are better than 95% as efficient--tankage for them masses much less. Further, another key advantage is that they do not need ignition: hypergolic means that if the two substances are put in contact, they will burn with no outside trigger. N2O4 is the storable oxidizer used; the latest MFS will include four different fuels to go with it. UDMH is the most stable and least-high-performing fuel, and least dense; Hydrazine is the highest-performing and densest but is unstable and thus not often suitable for use. Aerozine-50 is a 50-50 blend of the two; it's what the Titan II-IV and Proton and Apollo SPS all use. It performs quite well, and has a density between the two. MMH is often used as the fuel for maneuvering systems (Gemini and Shuttle OMS for example); it is about the same performance in terms of specific impulse as AZ-50 but rather denser, leading to a better-performing stage all-around. Given their storability and hypergolic nature, hypergolics are the fuel of choice for upper stages, maneuverinig systems, and bipropellant RCS, though some hypergolic lower stages (Proton, Titan) have been used.
=========================
On Tech Levels and Engine Types:

I implemented the system before .22 hit, and since I've been busy rescaling Kerbin I haven't integrated it into the tech tree.
First, let me run down how the system works; second, I'll tell you how I recommend you use it.
In order to have realistic engine performance, MFSC divides engines into types and tech levels. While type cannot be changed in game (it is determined by chamber pressure, area ratio, cycle type, etc., all abstracted as "engine type" and applied, as well as can be done given how unrealistic most KSP engine models are, to most stock and mod engines), tech level can. Tech level represents the advances that are applied to a specific model of engine over time.
For example, the venerable LR87 used in the Titan rocket went through upwards of 11 revisions over time, and was converted to use all three main fuel types (kerolox, i.e. Liquid Fuel [kerosene] and Liquid Oxygen; hypergolic NTO / Aerozine-50, simulated through the similar NTO/MMH combination ingame; and hydrolox, i..e. Liquid Hydrogen and LOX). Its Isp and thrust-to-weight ratio increased considerably through revision.
Terms: SL = sea level, TWR = thrust to weight ratio. Note that in MFSC, all engines include the mass of their thrust plate, so MFSC TWRs will be about 20-30% lower than real life engine stats.
So, first MFSC classifies each engine it supports by type. The types are:
O = Orbital maneuvering system (like the Apollo SPS or the Shuttle OMS, designed for tons and tons of restarts, and vacuum-only use). High vac Isp, very poor SL isp, lowest TWR. Example: LV-1, LV-909, Poodle. Usually hypergolic.
U = Upper stage. At most a couple restarts. Same Vac Isp as O, better SL Isp, better TWR. Like the Titan's LR-91. KSP doesn't really have any of these, they'd somewhere between the LV-909 and the LV-T45. Given the way it's shaped, I made the Skipper one, for example.
U+ = Upper stage optimized for vac use. Highest Vac Isp (higher than O). Lower TWR than U. That's what + means, higher vac, lower SL, lower TWR. Real life: Centaur's RL-10. KSP: doesn't have any examples, but KW's Apollo-SPS lookalike performs like one (though it is properly made Type O by me). Hydrolox is the fuel mode of choice.
L = lower stage. No restarts. Lowest Vac Isp, highest SL Isp barring Aerospike, highest TWR. Real life: Saturn V's F-1. Stock KSP: the Mainsail, obviously. (Although with its Isp unchanged, it's really an L+, so that's how I rate it.) Fuel is usually kerolox, though could be hypergolic or even hydrolox.
L+ = same changes as U+: higher IspV, lower IspSL, lower TWR. Designed for a single-stage-to-orbit stack (or at least an engine that's never staged away even if boosters are). Example: Space Shuttle SSME. KSP example: LV-T45.
A = Aerospike. Note that in real life nozzle losses are only 15% or so, and most of the efficiency loss at sea level is because there's air there, not because the nozzle is the wrong shape. For now they have the Isp of a U in vacuum, and 0.9x that at sea level. Real life examples: J-2T-250k (plug nozzle mod of the Saturn V J-2), the linear aerospike on the X-33. KSP Examples: obvious.
S = Solid.
S+ = Solid for vac use, lower SL, higher Vac Isp.

Note that I haven't integrated support for HoneyFox's engine ignition mod, so all the talk of restarts is just for information purposes.

What Isp an engine has is determind by grabbing its type, checking its tech level, and getting the appropriate entry in the TLTIsps area of RealSettings.cfg. Then any appropriate multipliers are applied: Hydrolox gets 1.3x to Vac Isp and an engine-specific amount (usually less than 1.3) to IspSL. Hypergolic fuel mode gets only 95% of the Isp in either case.

Engines have minimum tech levels; they aren't available before that. You can, however, upgrade past that.
===============================================
Now, as to how I recommend using tech levels. Note that the KSP tech tree has, roughly speaking, tiers, going from left to right. Start = TL0. When you've researched all techs in the tier after that, you get TL1. When you've researched all techs in the tier after that, you get access to TL2. And so forth. If there are any remaining TLs, you get them all at the end, or impose a delay on yourself.
(Eventually I will make this happen by code. For now, you're on your honor.)

You ARE, however, highly encourage to upgrade any engine, after placing it in the VAB, to your maximum available TL. So if you've researched 3 tiers of KSP techs, upgrade that LV-T45 to TL3! Otherwise your Isp will be horrific.

Rough TL to year comparison:
TL0: 1945+, WW2 and immediate postwar
TL1: 1955+, early Space Age rockets, Sputnik
TL2: 1960+, Mercury
TL3: 1963+, Gemini, early Apollo stuff
TL4: 1968+, Late Apollo
TL5: 1972+, Apollo Applications Program, etc.
TL6: 1980+, the Shuttle era
Tl7: The present day.