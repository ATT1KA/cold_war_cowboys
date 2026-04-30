using CWC.Game;

/// <summary>
/// S&box scene component that hosts the GameManager. Wired to the StartupScene
/// (scenes/minimal.scene). Sprint 5's Razor UI panels resolve this component
/// to read/write through the GameManager.
/// </summary>
public sealed class CwcGame : Component
{
	[Property] public ulong Seed { get; set; } = 1;

	public GameManager? Game { get; private set; }

	protected override void OnAwake()
	{
		Game = new GameManager();
		Game.NewGame( Seed );
	}

	protected override void OnUpdate()
	{
		// Phase progression is driven by UI buttons in Sprint 5; nothing to tick here yet.
	}
}
