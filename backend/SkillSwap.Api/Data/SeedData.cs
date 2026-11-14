using Microsoft.AspNetCore.Identity;

namespace SkillSwap.Api.Data;

/// <summary>
/// Seeds a skill catalog and a small demo community. Designed so the demo has
/// obvious mutual matches (Ada <-> Tunde, Chioma <-> Emeka, ...) from the first launch.
/// All demo accounts use the password below.
/// </summary>
public static class SeedData
{
    public const string DemoPassword = "Password123!";

    record Spec(string Name, string Email, string City, string Bio,
        (string Skill, SkillLevel Level)[] Offers, string[] Wants);

    static (string, SkillLevel) Adv(string n) => (n, SkillLevel.Advanced);
    static (string, SkillLevel) Mid(string n) => (n, SkillLevel.Intermediate);

    static readonly (string Name, string Category)[] Catalog =
    {
        ("Excel", "Data"), ("SQL", "Data"), ("Power BI", "Data"), ("Python", "Data"), ("Data Analysis", "Data"),
        ("Graphic Design", "Creative"), ("Figma", "Creative"), ("Photoshop", "Creative"), ("Video Editing", "Creative"),
        ("Photography", "Creative"), ("Illustration", "Creative"),
        ("Web Development", "Tech"), ("JavaScript", "Tech"), ("C#", "Tech"), ("UI/UX Design", "Tech"), ("Mobile Development", "Tech"),
        ("Public Speaking", "Business"), ("Bookkeeping", "Business"), ("Digital Marketing", "Business"),
        ("Copywriting", "Business"), ("Project Management", "Business"),
        ("French", "Languages"), ("Spanish", "Languages"), ("Yoruba", "Languages"),
        ("English Conversation", "Languages"), ("Igbo", "Languages"),
        ("Guitar", "Music"), ("Piano", "Music"), ("Singing", "Music"),
        ("Cooking", "Lifestyle"), ("Baking", "Lifestyle"), ("Fitness Coaching", "Lifestyle"),
        ("Fashion Design", "Lifestyle"), ("Tailoring", "Lifestyle"), ("Makeup", "Lifestyle"),
    };

    static readonly Spec[] Specs =
    {
        new("Ada Okafor", "ada@skillswap.demo", "Lagos", "Finance analyst who loves spreadsheets and wants to get creative.",
            new[] { Adv("Excel"), Mid("Data Analysis") }, new[] { "Graphic Design", "Figma" }),
        new("Tunde Bello", "tunde@skillswap.demo", "Lagos", "Freelance designer. Happy to trade design for spreadsheet skills.",
            new[] { Adv("Graphic Design"), Adv("Photoshop") }, new[] { "Excel", "Bookkeeping" }),
        new("Chioma Eze", "chioma@skillswap.demo", "Abuja", "French teacher and conference MC.",
            new[] { Adv("French"), Mid("Public Speaking") }, new[] { "Web Development", "Python" }),
        new("Emeka Nwosu", "emeka@skillswap.demo", "Lagos", "Full-stack developer who wants to speak with more confidence.",
            new[] { Adv("Web Development"), Adv("JavaScript"), Mid("C#") }, new[] { "Public Speaking", "French" }),
        new("Fatima Yusuf", "fatima@skillswap.demo", "Kano", "Home chef with a growing food business.",
            new[] { Adv("Cooking"), Adv("Baking") }, new[] { "Digital Marketing", "Photography" }),
        new("Seyi Adeyemi", "seyi@skillswap.demo", "Lagos", "Marketer who can't boil an egg.",
            new[] { Adv("Digital Marketing"), Mid("Copywriting") }, new[] { "Cooking", "Video Editing" }),
        new("Ngozi Obi", "ngozi@skillswap.demo", "Enugu", "Singer and guitarist learning data skills.",
            new[] { Mid("Guitar"), Adv("Singing") }, new[] { "Excel", "SQL" }),
        new("Kunle Balogun", "kunle@skillswap.demo", "Ibadan", "Data engineer by day, aspiring musician by night.",
            new[] { Adv("SQL"), Mid("Power BI"), Adv("Python") }, new[] { "Guitar", "Piano" }),
        new("Amaka Johnson", "amaka@skillswap.demo", "Lagos", "Photographer and video editor.",
            new[] { Adv("Photography"), Mid("Video Editing") }, new[] { "Digital Marketing", "UI/UX Design" }),
        new("Ibrahim Musa", "ibrahim@skillswap.demo", "Abuja", "Product designer exploring photography.",
            new[] { Adv("UI/UX Design"), Adv("Figma") }, new[] { "Photography", "C#" }),
        new("Blessing Udo", "blessing@skillswap.demo", "Port Harcourt", "Fashion designer building her brand.",
            new[] { Adv("Fashion Design"), Adv("Tailoring") }, new[] { "Graphic Design", "Bookkeeping" }),
        new("David Okon", "david@skillswap.demo", "Lagos", "Accountant who wants to learn to sew and code.",
            new[] { Adv("Bookkeeping"), Mid("Project Management") }, new[] { "Tailoring", "Web Development" }),
        new("Zainab Lawal", "zainab@skillswap.demo", "Lagos", "Language lover and community organiser.",
            new[] { Adv("Yoruba"), Adv("English Conversation") }, new[] { "French", "Spanish" }),
        new("Peter Alade", "peter@skillswap.demo", "Lagos", "Pianist and Spanish speaker, learning to code.",
            new[] { Mid("Spanish"), Adv("Piano") }, new[] { "Python", "Data Analysis" }),
    };

    public static async Task RunAsync(IServiceProvider sp, bool seedDemoUsers)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var userManager = sp.GetRequiredService<UserManager<AppUser>>();

        if (!await db.Skills.AnyAsync())
        {
            db.Skills.AddRange(Catalog.Select(c => new Skill { Name = c.Name, Category = c.Category }));
            await db.SaveChangesAsync();
        }

        if (!seedDemoUsers || await db.Users.AnyAsync()) return;   // already seeded

        var skillIds = await db.Skills.ToDictionaryAsync(s => s.Name, s => s.Id);
        var now = DateTime.UtcNow;
        var people = new Dictionary<string, AppUser>();

        for (var i = 0; i < Specs.Length; i++)
        {
            var s = Specs[i];
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = s.Email,
                Email = s.Email,
                EmailConfirmed = true,
                Name = s.Name,
                Bio = s.Bio,
                Location = s.City + ", Nigeria",
                TimeZone = "Africa/Lagos",
                JoinedAt = now.AddDays(-(Specs.Length - i) * 3)   // spread joins so "New members" has data
            };

            var result = await userManager.CreateAsync(user, DemoPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Seeding {s.Email} failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");

            people[s.Email] = user;

            foreach (var (skill, level) in s.Offers)
                db.UserSkills.Add(new UserSkill { UserId = user.Id, SkillId = skillIds[skill], Type = SkillType.Offering, Level = level });

            foreach (var skill in s.Wants)
                db.UserSkills.Add(new UserSkill { UserId = user.Id, SkillId = skillIds[skill], Type = SkillType.Wanted, Level = SkillLevel.Beginner });
        }
        await db.SaveChangesAsync();

        Guid Id(string key) => people[$"{key}@skillswap.demo"].Id;

        Swap MakeSwap(string a, string b, string teachA, string teachB, SwapStatus status, int daysAgo)
        {
            var created = now.AddDays(-daysAgo);
            var done = status == SwapStatus.Completed;
            return new Swap
            {
                ProposerId = Id(a), ReceiverId = Id(b),
                ProposerTeachesId = skillIds[teachA], ReceiverTeachesId = skillIds[teachB],
                HoursEach = 4, Message = "Let's swap!", Status = status,
                ProposerCompleted = done, ReceiverCompleted = done,
                CreatedAt = created, AcceptedAt = created.AddHours(3),
                CompletedAt = done ? created.AddDays(14) : null
            };
        }

        // Two completed swaps (gives some users ratings), one active swap (chat demo later).
        var cookingForMarketing = MakeSwap("fatima", "seyi", "Cooking", "Digital Marketing", SwapStatus.Completed, 30);
        var photoForDesign = MakeSwap("amaka", "ibrahim", "Photography", "UI/UX Design", SwapStatus.Completed, 25);
        var guitarForSql = MakeSwap("ngozi", "kunle", "Guitar", "SQL", SwapStatus.Active, 4);
        db.Swaps.AddRange(cookingForMarketing, photoForDesign, guitarForSql);

        db.Reviews.AddRange(
            new Review { SwapId = cookingForMarketing.Id, ReviewerId = Id("fatima"), RevieweeId = Id("seyi"), Rating = 5, Comment = "Seyi turned my Instagram around in four sessions.", CreatedAt = now.AddDays(-15) },
            new Review { SwapId = cookingForMarketing.Id, ReviewerId = Id("seyi"), RevieweeId = Id("fatima"), Rating = 5, Comment = "Best jollof lesson of my life. Patient teacher.", CreatedAt = now.AddDays(-15) },
            new Review { SwapId = photoForDesign.Id, ReviewerId = Id("amaka"), RevieweeId = Id("ibrahim"), Rating = 4, Comment = "Clear explanations and great feedback on my portfolio.", CreatedAt = now.AddDays(-10) },
            new Review { SwapId = photoForDesign.Id, ReviewerId = Id("ibrahim"), RevieweeId = Id("amaka"), Rating = 5, Comment = "Amaka made lighting finally click for me.", CreatedAt = now.AddDays(-10) });

        db.Messages.AddRange(
            new Message { SwapId = guitarForSql.Id, SenderId = Id("ngozi"), Text = "Hi Kunle! Excited to start. When are you free this week?", SentAt = now.AddDays(-3) },
            new Message { SwapId = guitarForSql.Id, SenderId = Id("kunle"), Text = "Hey Ngozi! Thursday evening works. Bring your guitar, I'll bring my laptop.", SentAt = now.AddDays(-3).AddMinutes(20) },
            new Message { SwapId = guitarForSql.Id, SenderId = Id("ngozi"), Text = "Perfect. See you then.", SentAt = now.AddDays(-3).AddMinutes(25) });

        // Pre-warm Ada's behavior so the discover page isn't empty on first demo.
        void Evt(EventType type, string? skill, string? target, string? query, double daysAgo) =>
            db.UserEvents.Add(new UserEvent
            {
                UserId = Id("ada"), Type = type,
                SkillId = skill is null ? null : skillIds[skill],
                TargetUserId = target is null ? null : Id(target),
                Query = query, Weight = EventWeights.For(type),
                CreatedAt = now.AddDays(-daysAgo)
            });

        Evt(EventType.AddWanted, "Graphic Design", null, null, 6);
        Evt(EventType.AddWanted, "Figma", null, null, 6);
        Evt(EventType.Search, "Graphic Design", null, "Graphic Design", 2);
        Evt(EventType.ViewProfile, "Graphic Design", "tunde", null, 2);
        Evt(EventType.Search, "UI/UX Design", null, "UI/UX Design", 1);
        Evt(EventType.ViewProfile, "Figma", "ibrahim", null, 1);
        Evt(EventType.Search, "Photography", null, "Photography", 0.5);

        await db.SaveChangesAsync();
    }
}
