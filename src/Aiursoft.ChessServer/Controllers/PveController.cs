using Aiursoft.AiurObserver;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.CSTools.Services;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.ChessServer.Services;
using Chess;

namespace Aiursoft.ChessServer.Controllers;

[Route("pve")]
public class PveController(
    ILogger<PveController> logger,
    ChessEngine engine,
    Counter counter,
    InMemoryDatabase database) : Controller
{
    [Route("new")]
    [HttpGet]
    public async Task<IActionResult> New(Guid playerId, int difficulty = 1)
    {
        if (difficulty > 15) difficulty = 15;
        
        // Add a computer player
        logger.LogInformation("Creating a new PVE game for player {playerId}.", playerId);
        var computerId = Guid.NewGuid();
        database.GetOrAddPlayer(computerId).NickName = engine.GetComputerName(difficulty) + " AI";
        //var asyncLock = new SemaphoreSlim(1, 1);

        // Create a challenge
        var player = database.GetOrAddPlayer(playerId);
        var challenge = new Challenge(
            creator: player, 
            message: "Play against computer", 
            roleRule: RoleRule.CreatorWhite,
            timeLimit: TimeSpan.MaxValue, 
            permission: ChallengePermission.Public);
        var challengeId = counter.GetUniqueNo();
        database.CreateChallenge(challengeId, challenge);
        
        // Let the computer accept the challenge
        await database.PatchChallengeAsAcceptedAsync(challengeId, computerId);

        var acceptedChallenge = database.GetAcceptedChallenge(challengeId);
        if (acceptedChallenge == null)
        {
            return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id = challengeId });
        }
        
        // Let computer respond to the player's move.
        var moveLock = new SemaphoreSlim(1, 1);
        ISubscription? subscription = null;
        subscription = acceptedChallenge.Game.FenChangedChannel.Subscribe(async fen =>
        {
            try
            {
                if (ChessBoard.LoadFromFen(fen).Turn != PieceColor.Black)
                {
                    return;
                }

                await moveLock.WaitAsync();
                try
                {
                    logger.LogInformation("The fen {fen} means it's the computer's turn. Computer is calculating the best move.", fen);
                    // Wait for the UI to update
                    await Task.Delay(300);
                    var bestMove = engine.GetBestMove(fen, difficulty);

                    lock (acceptedChallenge.Game.MovePieceLock)
                    {
                        if (!acceptedChallenge.Game.Board.IsEndGame && acceptedChallenge.Game.Board.IsValidMove(bestMove))
                        {
                            acceptedChallenge.Game.Board.Move(bestMove);
                        }
                    }

                    logger.LogInformation("Computer calculated the best move: {bestMove}", bestMove);
                    await acceptedChallenge.Game.FenChangedChannel.BroadcastAsync(acceptedChallenge.Game.Board.ToFen());
                }
                finally
                {
                    moveLock.Release();
                }

                if (acceptedChallenge.Game.Board.IsEndGame)
                {
                    subscription?.Unsubscribe();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Computer player failed to process a move for challenge {challengeId}.", challengeId);
            }
        });

        _ = Task.Run(async () =>
        {
            await acceptedChallenge.Game.FenChangedChannel.BroadcastAsync(acceptedChallenge.Game.Board.ToFen());
        });
        
        // Redirect to the game page
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id = challengeId });
    }
}
