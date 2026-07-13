using System;
using Npgsql;

var connectionString = "Host=localhost;Port=5432;Database=fatawa;Username=postgres;Password=postgres";

try 
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();
    
    Console.WriteLine("========================================");
    Console.WriteLine("DATABASE CHECK");
    Console.WriteLine("========================================\n");
    
    // 1. Total fatwas
    using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM fatwas", conn))
    {
        var total = (long)cmd.ExecuteScalar()!;
        Console.WriteLine($"Total fatwas: {total}");
    }
    
    // 2. Fatwas with embeddings
    using (var cmd = new NpgsqlCommand(@"
        SELECT 
            COUNT(*) as total,
            COUNT(embedding_title) as with_title_embedding,
            COUNT(embedding_question) as with_question_embedding
        FROM fatwas", conn))
    {
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            Console.WriteLine($"With title embedding: {reader.GetInt64(1)}");
            Console.WriteLine($"With question embedding: {reader.GetInt64(2)}");
        }
    }
    
    Console.WriteLine("\n--- Prayer-related fatwas ---");
    
    // 3. Prayer-related fatwas
    using (var cmd = new NpgsqlCommand(@"
        SELECT fatwa_id, collection_type, title, left(question, 100) as question_preview
        FROM fatwas 
        WHERE title ILIKE '%صلاة%' 
           OR title ILIKE '%صلوة%'
           OR question ILIKE '%صلاة%'
        LIMIT 10", conn))
    {
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read())
        {
            count++;
            Console.WriteLine($"\n[{count}] Fatwa #{reader.GetInt64(0)}");
            Console.WriteLine($"    Collection: {reader.GetString(1)}");
            Console.WriteLine($"    Title: {reader.GetString(2)}");
            Console.WriteLine($"    Question: {reader.GetString(3)}...");
        }
        
        if (count == 0)
        {
            Console.WriteLine("❌ No prayer-related fatwas found!");
        }
        else
        {
            Console.WriteLine($"\n✓ Found {count} prayer-related fatwas");
        }
    }
    
    Console.WriteLine("\n--- أذكار (remembrances) related fatwas ---");
    
    // 4. Adhkar-related fatwas
    using (var cmd = new NpgsqlCommand(@"
        SELECT fatwa_id, title, left(question, 100) as question_preview
        FROM fatwas 
        WHERE title ILIKE '%ذكر%' 
           OR title ILIKE '%أذكار%'
           OR question ILIKE '%ذكر%'
        LIMIT 5", conn))
    {
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read())
        {
            count++;
            Console.WriteLine($"\n[{count}] Fatwa #{reader.GetInt64(0)}");
            Console.WriteLine($"    Title: {reader.GetString(1)}");
            Console.WriteLine($"    Question: {reader.GetString(2)}...");
        }
        
        if (count == 0)
        {
            Console.WriteLine("❌ No adhkar-related fatwas found!");
        }
        else
        {
            Console.WriteLine($"\n✓ Found {count} adhkar-related fatwas");
        }
    }
    
    Console.WriteLine("\n========================================");
    Console.WriteLine("Database check complete!");
    Console.WriteLine("========================================");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ ERROR: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}

