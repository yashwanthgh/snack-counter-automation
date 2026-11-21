using System.Text.Json;

using SnackCounter;

namespace SnackCounter.ChatSim;

// Reads config/people.json: who works where, who volunteers, who is away or visiting.
//
// In production this would NOT be a file: people come from the company directory (company login),
// and "away" / "visiting" come from the leave calendar or from chat commands like "/snack away 5".
// The file stands in for those sources, the same way FakeChat stands in for the chat tool.
public class PeopleLoader
{
    public static List<Employee> LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException("People file not found: " + path);
        }
        return LoadFromJson(File.ReadAllText(path));
    }

    public static List<Employee> LoadFromJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        List<Employee> people = new List<Employee>();

        foreach (JsonElement p in doc.RootElement.EnumerateArray())
        {
            string id = p.GetProperty("id").GetString() ?? "";
            string name = p.GetProperty("name").GetString() ?? id;
            string officeText = p.GetProperty("homeOffice").GetString() ?? "";
            bool isVolunteer = p.GetProperty("isVolunteer").GetBoolean();

            if (id == "")
            {
                throw new InvalidDataException("A person in people.json has no id");
            }
            if (!Enum.TryParse(officeText, true, out Office homeOffice))
            {
                throw new InvalidDataException("Unknown office '" + officeText + "' for " + id);
            }
            foreach (Employee existing in people)
            {
                if (existing.EmployeeId == id)
                {
                    throw new InvalidDataException("Duplicate id in people.json: " + id);
                }
            }

            Employee employee = new Employee(id, name, homeOffice, isVolunteer);
            employee.IsAdmin = p.TryGetProperty("isAdmin", out JsonElement admin) && admin.GetBoolean();

            // Optional: out of office until a date.
            employee.UnavailableUntil = OptionalDate(p, "awayUntil");

            // Optional: visiting the other office until a date.
            if (p.TryGetProperty("visitingOffice", out JsonElement visiting) && visiting.ValueKind == JsonValueKind.String)
            {
                if (!Enum.TryParse(visiting.GetString(), true, out Office visitingOffice))
                {
                    throw new InvalidDataException("Unknown visiting office for " + id);
                }
                employee.VisitingOffice = visitingOffice;
                employee.VisitingUntil = OptionalDate(p, "visitingUntil");
            }

            people.Add(employee);
        }

        return people;
    }

    private static DateTimeOffset? OptionalDate(JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.Parse(value.GetString()!);
        }
        return null;
    }
}
