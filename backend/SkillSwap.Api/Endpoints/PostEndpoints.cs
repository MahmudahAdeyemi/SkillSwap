using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class PostEndpoints
{
    public static void MapPosts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/posts").WithTags("Community");

        // GET /api/posts?category=&authorId=&query=&page=1&pageSize=20
        g.MapGet("", async (ClaimsPrincipal p, AppDbContext db, UserMapper mapper,
            string? category, Guid? authorId, string? query, bool? following,
            int page = 1, int pageSize = 20) =>
        {
            var me = p.Identity?.IsAuthenticated == true ? p.GetUserId() : (Guid?)null;

            if (following == true && !me.HasValue)
                return Results.Unauthorized();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var q = db.Posts.AsNoTracking().AsQueryable();
            if (authorId.HasValue)
                q = q.Where(x => x.AuthorId == authorId.Value);
            if (!string.IsNullOrWhiteSpace(category) && category.ToLower() != "all")
            {
                var c = category.Trim().ToLower();
                q = q.Where(x => x.Category.ToLower() == c);
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                var text = query.Trim().ToLower();
                q = q.Where(x => x.Content.ToLower().Contains(text) ||
                                 x.Category.ToLower().Contains(text));
            }
            if (following == true)
            {
                var followingIds = db.Follows.AsNoTracking()
                    .Where(f => f.FollowerId == me!.Value)
                    .Select(f => f.FollowingId);
                q = q.Where(x => followingIds.Contains(x.AuthorId));
            }

            var total = await q.CountAsync();
            var posts = await q.OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var authorIds = posts.Select(x => x.AuthorId).Distinct();
            var authors = await mapper.GetManyAsync(authorIds);

            List<Guid> likedPostIds = [];
            if (me.HasValue)
            {
                var postIds = posts.Select(x => x.Id).ToList();
                likedPostIds = await db.PostLikes
                    .Where(l => l.UserId == me.Value && postIds.Contains(l.PostId))
                    .Select(l => l.PostId)
                    .ToListAsync();
            }

            var dtos = posts.Select(post => new CommunityPostDto(
                post.Id,
                authors[post.AuthorId],
                post.Category,
                post.Content,
                post.ImageUrl,
                post.LikesCount,
                post.CommentsCount,
                likedPostIds.Contains(post.Id),
                post.CreatedAt
            )).ToList();

            return Results.Ok(new PagedResult<CommunityPostDto>(dtos, page, pageSize, total));
        })
        .AllowAnonymous();

        // GET /api/posts/{id}
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDbContext db, UserMapper mapper) =>
        {
            var me = p.Identity?.IsAuthenticated == true ? p.GetUserId() : (Guid?)null;
            var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (post is null) return Results.NotFound();

            var author = await mapper.GetAsync(post.AuthorId);
            if (author is null) return Results.NotFound();

            var liked = me.HasValue && await db.PostLikes
                .AnyAsync(l => l.PostId == id && l.UserId == me.Value);

            return Results.Ok(new CommunityPostDto(
                post.Id, author, post.Category, post.Content, post.ImageUrl,
                post.LikesCount, post.CommentsCount, liked, post.CreatedAt));
        })
        .AllowAnonymous();

        // GET /api/posts/{id}/comments
        g.MapGet("/{id:guid}/comments", async (Guid id, AppDbContext db, UserMapper mapper,
            int page = 1, int pageSize = 20) =>
        {
            if (!await db.Posts.AnyAsync(x => x.Id == id))
                return Results.NotFound();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var q = db.PostComments.AsNoTracking().Where(x => x.PostId == id);
            var total = await q.CountAsync();
            var rows = await q.OrderBy(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var authors = await mapper.GetManyAsync(rows.Select(x => x.AuthorId));
            var items = rows
                .Where(x => authors.ContainsKey(x.AuthorId))
                .Select(x => new PostCommentDto(
                    x.Id, x.PostId, authors[x.AuthorId], x.Content, x.CreatedAt))
                .ToList();

            return Results.Ok(new PagedResult<PostCommentDto>(items, page, pageSize, total));
        })
        .AllowAnonymous();

        // POST /api/posts/{id}/comments
        g.MapPost("/{id:guid}/comments", async (Guid id, CreatePostCommentRequest req,
            ClaimsPrincipal p, AppDbContext db, UserMapper mapper) =>
        {
            var me = p.GetUserId();
            var post = await db.Posts.FindAsync(id);
            if (post is null) return Results.NotFound();

            var content = req.Content.Trim();
            if (content.Length == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["content"] = new[] { "Write something before posting your reply." }
                });

            var comment = new PostComment
            {
                PostId = id,
                AuthorId = me,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };
            db.PostComments.Add(comment);
            post.CommentsCount++;
            await db.SaveChangesAsync();

            var author = (await mapper.GetAsync(me))!;
            return Results.Created($"/api/posts/{id}/comments/{comment.Id}",
                new PostCommentDto(comment.Id, comment.PostId, author, comment.Content, comment.CreatedAt));
        })
        .RequireAuthorization()
        .Validate<CreatePostCommentRequest>();

        // POST /api/posts
        g.MapPost("", async (CreatePostRequest req, ClaimsPrincipal p, AppDbContext db, UserMapper mapper) =>
        {
            var me = p.GetUserId();
            var post = new Post
            {
                AuthorId = me,
                Category = req.Category.Trim().ToLower(),
                Content = req.Content.Trim(),
                ImageUrl = req.ImageUrl?.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            db.Posts.Add(post);
            await db.SaveChangesAsync();

            var author = (await mapper.GetAsync(me))!;
            return Results.Created($"/api/posts/{post.Id}",
                new CommunityPostDto(post.Id, author, post.Category, post.Content, post.ImageUrl, 0, 0, false, post.CreatedAt));
        })
        .RequireAuthorization()
        .Validate<CreatePostRequest>();

        // POST /api/posts/{id}/like
        g.MapPost("/{id:guid}/like", async (Guid id, ClaimsPrincipal p, AppDbContext db) =>
        {
            var me = p.GetUserId();
            var post = await db.Posts.FindAsync(id);
            if (post is null) return Results.NotFound();

            var existing = await db.PostLikes.FirstOrDefaultAsync(l => l.PostId == id && l.UserId == me);
            if (existing is not null)
            {
                db.PostLikes.Remove(existing);
                post.LikesCount = Math.Max(0, post.LikesCount - 1);
            }
            else
            {
                db.PostLikes.Add(new PostLike { PostId = id, UserId = me, CreatedAt = DateTime.UtcNow });
                post.LikesCount++;
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization();
    }
}
