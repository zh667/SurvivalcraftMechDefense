using Game;

namespace LabBlocks;

// A stand-in for "another mod with new blocks" (1.0.0 uninstall test, tools/run_isolated.py uninstall --extra-blocks): three plain cubes
// with dynamic block numbers. Installed only into an isolated runtime, never shipped.
public abstract class LabCube : CubeBlock {
    public override void Initialize(){DefaultDisplayName="实验方块 "+GetType().Name[^1];DefaultDescription="Isolated test block";DefaultCategory="Terrain";DefaultTextureSlot=GetType().Name[^1]-'A'+1;base.Initialize();}
}
public class LabBlockA : LabCube { }
public class LabBlockB : LabCube { }
public class LabBlockC : LabCube { }
