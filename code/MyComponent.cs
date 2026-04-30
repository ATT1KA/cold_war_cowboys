using CWC.Game;
using CWC.UI;

/// <summary>
/// S&box scene component that hosts the GameManager + ViewModel. Wired to
/// the StartupScene (scenes/minimal.scene). Razor panels resolve this
/// component (or its ViewModel) to read/write through the game core.
/// </summary>
public sealed class CwcGame : Component
{
	[Property] public ulong Seed { get; set; } = 1;

	public GameManager? Game { get; private set; }
	public GameViewModel? ViewModel { get; private set; }

	protected override void OnAwake()
	{
		Game = new GameManager();
		Game.NewGame( Seed );
		ViewModel = new GameViewModel( Game );
	}

	protected override void OnUpdate()
	{
		// Phase progression is driven by UI buttons; nothing to tick here.
	}
}
