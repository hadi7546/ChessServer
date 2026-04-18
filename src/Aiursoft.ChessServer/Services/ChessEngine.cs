using System.Threading.Channels;
using Lynx;
using Lynx.Model;

namespace Aiursoft.ChessServer.Services;

public class ChessEngine
{
    private readonly Lazy<Engine> _engine;
    private readonly object _engineLock = new();

    public ChessEngine()
    {
        var channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _engine = new Lazy<Engine>(() => new Engine(channel.Writer));
    }
    
    public string GetComputerName(int difficulty)
    {
        return difficulty switch
        {
            1 => "Chimpanzee",
            2 => "Easy",
            3 => "Intermediate",
            4 => "Hard",
            5 => "Brutal",
            6 => "Grandmaster",
            7 => "Torture",
            8 => "Unbeatable",
            _ => "Unknown"
        };
    }

    public string GetBestMove(string fen, int difficulty)
    {
        lock (_engineLock)
        {
            var engine = _engine.Value;
            engine.AdjustPosition($"position fen {fen}");
            var positionClone = new Position(engine.Game.CurrentPosition);

            return engine.BestMove(new($"go depth {difficulty}"))
                .BestMove
                .ToEPDString(positionClone);
        }
    }
}
