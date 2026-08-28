using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniMiki.Models
{
    /// <summary>Kök nesne: mini_miki_poland_ecommerce_legal_dataset.json ile birebir eşleşir.</summary>
    public sealed class LegalDataset
    {
        [JsonPropertyName("metadata")]
        public DatasetMetadata Metadata { get; set; } = new();

        [JsonPropertyName("documents")]
        public List<LegalDocumentChunk> Documents { get; set; } = new();
    }

    public sealed class DatasetMetadata
    {
        [JsonPropertyName("dataset_name")]
        public string DatasetName { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("target_market")]
        public string TargetMarket { get; set; } = string.Empty;

        [JsonPropertyName("content_language")]
        public string ContentLanguage { get; set; } = string.Empty;

        [JsonPropertyName("source_language")]
        public string SourceLanguage { get; set; } = string.Empty;

        [JsonPropertyName("generated_date")]
        public DateOnly GeneratedDate { get; set; }

        [JsonPropertyName("prepared_for")]
        public string PreparedFor { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("disclaimer")]
        public string Disclaimer { get; set; } = string.Empty;

        [JsonPropertyName("primary_legal_sources")]
        public List<string> PrimaryLegalSources { get; set; } = new();

        [JsonPropertyName("schema_notes")]
        public string SchemaNotes { get; set; } = string.Empty;
    }

    /// <summary>Vektör veritabanına yüklenecek tek bir hukuki metin parçası (chunk).</summary>
    public sealed class LegalDocumentChunk
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("topic")]
        public string Topic { get; set; } = string.Empty;

        [JsonPropertyName("subtopic")]
        public string Subtopic { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();

        [JsonPropertyName("legal_references")]
        public List<string> LegalReferences { get; set; } = new();

        [JsonPropertyName("source_urls")]
        public List<string> SourceUrls { get; set; } = new();

        // Bazı kayıtlarda null olabiliyor (örn. PL-MP-001, PL-MP-004, PL-MP-005) -> nullable şart.
        [JsonPropertyName("effective_date")]
        public DateOnly? EffectiveDate { get; set; }

        [JsonPropertyName("last_verified_date")]
        public DateOnly LastVerifiedDate { get; set; }

        [JsonPropertyName("importance")]
        public ImportanceLevel Importance { get; set; }
    }

    // Not: .NET 6/7/8 hepsinde çalışır. Deserialize zaten case-insensitive'tir,
    // CamelCase policy "Critical" -> "critical" yönünde serialize ederken de tutarlılık sağlar.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ImportanceLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
}
