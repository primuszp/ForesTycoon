# Arbaro tree parameter sets

Reference parameter files copied unchanged from the Arbaro project
(https://github.com/wdiestel/arbaro, `trees/`, Wolfram Diestel, GNU GPL v2; full text in
`LICENSE.txt`). They define trees for the Weber & Penn generator in Arbaro's XML format.

The game's species sets (`ForesTycoon.TreeModels/Architecture/Presets/*.xml`) are derived from them and use the
same format, so any of these files can be opened in Arbaro to compare. The tests load all files here
through the game's generator (`TreeArchitecture.SkeletonFromXml`), and `TreePreviewDump` renders them.
See the [parameter-set notes](../../docs/arbaro-parameter-sets.md),
[integration notice](../../ForesTycoon.TreeModels/Generation/Dendro/NOTICE.md), and
[replacement plan](../../docs/own-tree-generator-plan.md). The existing generator remains active;
the replacement is planned for later.
