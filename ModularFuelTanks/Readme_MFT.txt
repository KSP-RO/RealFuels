***Modular Fuel Tanks***
by taniwha, based on work by NathanKell and ChestBurster for Modular Fuel Systems Continued.

ialdabaoth (who is awesome) created Modular Fuels, and we're maintaining it in his absence.

License remains CC-BY-SA as modified by ialdabaoth.

Also included: Module Manager (by sarbian, based on ialdabaoth's work). See Module Manager thread for details and license and source: http://http://forum.kerbalspaceprogram.com/threads/55219
Module Manager is required for MFSC to work.

DESCRIPTION:
Modular Fuel Tanks allows any supported tank to be filled with exactly how much or how little fuel you want, of whatever type you want (though different tanks may allow or disallow certain fuels; jet fuel tanks won't take oxidizer for instance).

USAGE:
You can access all MFS-related GUIs ingame by going to the Action Group editor in the VAB/SPH and clicking on a tank. You will see a list of the resources you can fit in the tank, with their current amounts and max amounts (if any). You will also see the total mass of the tank, and the dry mass of the tank.
A special note regarding XenonGas: Since it is a gas, 1 unit of xenon takes less volume than other resources. Thus for a regular tank with 12.5 units of volume available, you'll still be able to fit in 700 units of Xenon.
ElectricCharge works in a similar manner: you can fit 60 units of EC in 1 unit of volume. Note that only tanks of type ServiceModule can hold ElectricCharge

INSTALL INSTRUCTIONS:
1. Delete any existing ModularFuelTanks or RealFuels folder in your KSP/GameData folder. This is VITAL.
2. Extract this archive to your KSP/GameData folder


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