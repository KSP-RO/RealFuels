needs review
Tank masses used to be calculated improperly, using the whole volume of the tank, instead of an area or wall volume, causing the tank's dry mass to increase as a cube, resulting in very poor performance in large rockets.

The new method is to calculate the wall volume of a spherical tank, then apply an aluminum density to it, resulting in an accurate dry mass, similar to numbers irl.
The method is not incredibly accurate, but it is a quick and dirty patch, so hopefully someone more skilled & knowledgeable of the codebase takes on the task to optimize and clean it up/make it play nice with older code.
