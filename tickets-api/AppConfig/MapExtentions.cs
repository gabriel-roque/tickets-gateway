using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketsApi.Models;

namespace TicketsApi.AppConfig;

internal static class MapExtensions
{
    internal static void Map<T>(this ModelBuilder modelBuilder, bool hasKey = true) where T : Entity
    {
        modelBuilder.Entity<T>(builder =>
        {
            if (hasKey)
                builder.HasKey(s => s.Id);
            else
                builder.Ignore(c => c.Id);

            builder.Property(r => r.CreationDate)
                .HasDefaultValueSql("getutcdate()")
                .ValueGeneratedOnAdd().Metadata
                .SetAfterSaveBehavior(PropertySaveBehavior.Save);

            builder.Property(r => r.UpdateDate)
                .HasDefaultValueSql("getutcdate()")
                .ValueGeneratedOnAddOrUpdate().Metadata
                .SetAfterSaveBehavior(PropertySaveBehavior.Save);

            builder.HasQueryFilter(e => EF.Property<bool>(e, "Deleted") == false);

            builder.Property(p => p.Deleted).IsRequired();
        });
    }

    internal static PropertyBuilder<string> DefaultString(
        this PropertyBuilder<string> builder,
        int maxLength = 256,
        bool required = false,
        TextType type = TextType.None)
    {
        return builder
            .HasMaxLength(maxLength)
            .HasConversion(
                c => HandleTo(c, maxLength, type, required),
                c => c)
            .IsRequired(required);
    }

    private static string HandleTo(string s, int maxLength, TextType type, bool required)
    {
        if (string.IsNullOrEmpty(s)) return required ? string.Empty : null;

        switch (type)
        {
            case TextType.UpperFirst:
                s = s.ToUpperFirst();
                break;
            case TextType.UpperFirstNoLower:
                s = s.ToUpperFirst(false);
                break;
            case TextType.UpperAllFirst:
                s = s.ToUpperAllFirst();
                break;
            case TextType.Numbers:
                s = s.GetNumbersForQuery();
                break;
            case TextType.UpperAllFirstNoLower:
                s = s.ToUpperAllFirst(false);
                break;
            case TextType.Upper:
                s = s.ToUpperInvariant();
                break;
            case TextType.NoSpecialCharsUpper:
                s = s.RemoveSpecialCharacters().ToUpperInvariant();
                break;
            case TextType.KeepNumbersSNToUpper:
                s = s.RemoveNumberSpecialCharacters().ToUpperInvariant();
                break;
            case TextType.NoSpecialChars:
                s = s.RemoveSpecialCharacters();
                break;
            case TextType.None:
            default:
                break;
        }
        
        if(type != TextType.Numbers){
            if (s.Length < maxLength)
                maxLength = s.Length;
            s = s.Substring(0, maxLength).Trim();
        }

        return s;
    }
}

internal enum TextType
{
    None,
    UpperFirst,
    UpperFirstNoLower,
    UpperAllFirst,
    UpperAllFirstNoLower,
    Upper,
    Numbers,
    NoSpecialCharsUpper,
    KeepNumbersSNToUpper,
    NoSpecialChars
}
