// using Microsoft.AspNetCore.Mvc;
// using Swashbuckle.AspNetCore.Annotations;
// using Youverse.Core.Services.Transit;
// using Youverse.Hosting.Controllers.Base;
//
// namespace Youverse.Hosting.Controllers.OwnerToken.Transit
// {
//     /// <summary>
//     /// Routes emoji requests from the owner app to a target identity
//     /// </summary>
//     [ApiController]
//     [Route(OwnerApiPathConstants.TransitQueryV1)]
//     [AuthorizeValidOwnerToken]
//     public class TransitEmojiController : OdinControllerBase
//     {
//         private readonly TransitQueryService _transitQueryService;
//
//         public TransitEmojiController(TransitQueryService transitQueryService)
//         {
//             _transitQueryService = transitQueryService;
//         }
//
//         /// <summary>
//         /// Adds an emoji reaction for a given file
//         /// </summary>
//         /// <param name="request"></param>
//         [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
//         [HttpPost("add")]
//         public IActionResult AddEmojiReaction([FromBody] AddReactionReqeust request)
//         {
//             base.AddReaction(request);
//             return NoContent();
//         }
//
//         /// <summary>
//         /// Adds an emoji reaction for a given file
//         /// </summary>
//         /// <param name="request"></param>
//         [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
//         [HttpPost("delete")]
//         public IActionResult DeleteEmojiReaction([FromBody] DeleteReactionRequest request)
//         {
//             base.DeleteReaction(request);
//             return NoContent();
//         }
//
//         /// <summary>
//         /// Adds an emoji reaction for a given file
//         /// </summary>
//         /// <param name="request"></param>
//         [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
//         [HttpPost("deleteall")]
//         public IActionResult DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
//         {
//             base.DeleteAllReactions(request);
//             return NoContent();
//         }
//
//
//         /// <summary />
//         [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
//         [HttpPost("list")]
//         public GetReactionsResponse GetAllReactions([FromBody] GetReactionsRequest request)
//         {
//             return base.GetReactions(request);
//         }
//
//         /// <summary>
//         /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
//         /// </summary>
//         [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
//         [HttpPost("summary")]
//         public GetReactionCountsResponse GetReactionCountsByFile([FromBody] GetReactionsRequest request)
//         {
//             return base.GetReactionCounts(request);
//         }
//
//         /// <summary>
//         /// Get reactions by identity and file
//         /// </summary>
//         [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
//         [HttpPost("listbyidentity")]
//         public List<string> GetReactionsByIdentity([FromBody] GetReactionsByIdentityRequest request)
//         {
//             return base.GetReactionsByIdentityAndFile(request);
//         }
//     }
// }