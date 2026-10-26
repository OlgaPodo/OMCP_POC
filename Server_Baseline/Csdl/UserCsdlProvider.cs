namespace GraphMcpServer.Csdl;

public static class UserCsdlProvider
{
    public static string GetCsdlJson()
    {
        return """
{
  "$Version": "4.01",
  "$EntityContainer": "Microsoft.Graph.GraphService",
  "Microsoft.Graph": {
    "User": {
      "$Kind": "EntityType",
      "$Key": [ "id" ],
      "id": { "$Type": "Edm.String", "$Nullable": false },
      "displayName": { "$Type": "Edm.String" },
      "userPrincipalName": { "$Type": "Edm.String" },
      "mail": { "$Type": "Edm.String", "$Nullable": true },
      "jobTitle": { "$Type": "Edm.String", "$Nullable": true },
      "department": { "$Type": "Edm.String", "$Nullable": true },
      "officeLocation": { "$Type": "Edm.String", "$Nullable": true },
      "mobilePhone": { "$Type": "Edm.String", "$Nullable": true },
      "manager": {
        "$Kind": "NavigationProperty",
        "$Type": "Microsoft.Graph.User"
      },
      "directReports": {
        "$Kind": "NavigationProperty",
        "$Collection": true,
        "$Type": "Microsoft.Graph.User"
      }
    },
    "GraphService": {
      "$Kind": "EntityContainer",
      "users": {
        "$Collection": true,
        "$Type": "Microsoft.Graph.User"
      },
      "me": {
        "$Type": "Microsoft.Graph.User"
      },
      "@Agent.Prompts": [
        {
          "Name": "whom_olga_reports_to",
          "Title": "Whom Olga reports to?",
          "Description": "Find organizational manager for Olga Podolyako or any given user.",
          "Arguments": [
            {
              "Name": "userName",
              "Description": "User display name or UPN",
              "Required": false,
              "Default": "Olga Podolyako"
            }
          ],
          "Messages": [
            {
              "Role": "user",
              "Content": {
                "Type": "text",
                "Text": "Whom does {userName} report to?"
              }
            }
          ]
        },
        {
          "Name": "who_are_olgas_peers",
          "Title": "Who are Olga's peers?",
          "Description": "Discover peer colleagues reporting to the same manager as Olga Podolyako.",
          "Arguments": [
            {
              "Name": "userName",
              "Description": "User display name or UPN",
              "Required": false,
              "Default": "Olga Podolyako"
            }
          ],
          "Messages": [
            {
              "Role": "user",
              "Content": {
                "Type": "text",
                "Text": "Who are {userName}'s peers?"
              }
            }
          ]
        },
        {
          "Name": "direct_reports_of_manager",
          "Title": "Direct reports of a manager",
          "Description": "List all members reporting directly to a given manager.",
          "Arguments": [
            {
              "Name": "managerNameOrId",
              "Description": "Manager's name, ID, or UPN",
              "Required": true,
              "Default": "Alex Wilbur"
            }
          ],
          "Messages": [
            {
              "Role": "user",
              "Content": {
                "Type": "text",
                "Text": "List direct reports for manager {managerNameOrId}."
              }
            }
          ]
        },
        {
          "Name": "search_users_by_dept_title",
          "Title": "Search users by department and title",
          "Description": "Search users filtered by organizational department and job title.",
          "Arguments": [
            {
              "Name": "department",
              "Description": "Department name",
              "Required": false,
              "Default": "Identity"
            },
            {
              "Name": "jobTitle",
              "Description": "Job title keyword",
              "Required": false,
              "Default": "Architect"
            }
          ],
          "Messages": [
            {
              "Role": "user",
              "Content": {
                "Type": "text",
                "Text": "Find users in department '{department}' with title '{jobTitle}'."
              }
            }
          ]
        }
      ]
    }
  }
}
""";
    }

    public static string GetCsdlXml()
    {
        return """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Microsoft.Graph" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityType Name="user">
        <Key>
          <PropertyRef Name="id" />
        </Key>
        <Property Name="id" Type="Edm.String" Nullable="false" />
        <Property Name="displayName" Type="Edm.String" />
        <Property Name="userPrincipalName" Type="Edm.String" />
        <Property Name="mail" Type="Edm.String" />
        <Property Name="jobTitle" Type="Edm.String" />
        <Property Name="department" Type="Edm.String" />
        <Property Name="officeLocation" Type="Edm.String" />
        <NavigationProperty Name="manager" Type="Microsoft.Graph.user" />
        <NavigationProperty Name="directReports" Type="Collection(Microsoft.Graph.user)" />
      </EntityType>

      <EntityContainer Name="GraphService">
        <EntitySet Name="users" EntityType="Microsoft.Graph.user">
          <NavigationPropertyBinding Path="manager" Target="users" />
          <NavigationPropertyBinding Path="directReports" Target="users" />
        </EntitySet>
        <Singleton Name="me" EntityType="Microsoft.Graph.user" />

        <!-- Baseline: Agent.Prompts annotation without Examples -->
        <Annotation Term="Agent.Prompts">
          <Collection>
            <Record Type="Agent.PromptType">
              <PropertyValue Property="Name" String="whom_olga_reports_to" />
              <PropertyValue Property="Title" String="Whom Olga reports to?" />
              <PropertyValue Property="Description" String="Find organizational manager for Olga Podolyako." />
              <PropertyValue Property="Arguments">
                <Collection>
                  <Record Type="Agent.PromptArgument">
                    <PropertyValue Property="Name" String="userName" />
                    <PropertyValue Property="Default" String="Olga Podolyako" />
                  </Record>
                </Collection>
              </PropertyValue>
              <PropertyValue Property="Messages">
                <Collection>
                  <Record Type="Agent.PromptMessage">
                    <PropertyValue Property="Role" String="user" />
                    <PropertyValue Property="Text" String="Whom does {userName} report to?" />
                  </Record>
                </Collection>
              </PropertyValue>
            </Record>
          </Collection>
        </Annotation>
      </EntityContainer>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
""";
    }

    public static object GetPromptArchitecturalAnalysis()
    {
        return new
        {
            topic1 = new
            {
                title = "Addressing Concern 1: Prompts as Search Index vs. Reusable Templates",
                problemStatement = "In early discussions, prompts were viewed as a semantic retrieval index to avoid exposing 100+ fine-grained tools. However, the MCP specification defines prompts strictly as parameterized message templates (like slash commands).",
                resolutionOption1 = "Option 1 embeds both capabilities into a unified annotation: The prompt defines the formal reusable template (Name, Arguments, Messages with role/text), while the 'Examples' collection provides utterance-to-query pairs. This allows the gateway to expose standard MCP prompts/list & prompts/get templates, while an LLM agent or semantic router can use the Examples collection as a zero-shot retrieval index to pick the right prompt or tool automatically."
            },
            topic2 = new
            {
                title = "Addressing Concern 2: The End-to-End Mapping Pipeline",
                stages = new[]
                {
                    new
                    {
                        stage = 1,
                        name = "OData CSDL Annotation",
                        description = "Service author annotates the EntityContainer with @Agent.Prompts (Option 1 format).",
                        exampleSnippet = "@Agent.Prompts: [{ Name: 'whom_olga_reports_to', Arguments: [...], Messages: [...], Examples: [...] }]"
                    },
                    new
                    {
                        stage = 2,
                        name = "MCP Protocol Projection",
                        description = "MCP Server automatically projects @Agent.Prompts to prompts/list and prompts/get responses.",
                        exampleSnippet = "{ name: 'whom_olga_reports_to', arguments: [{ name: 'userName' }], messages: [{ role: 'user', content: {...} }] }"
                    },
                    new
                    {
                        stage = 3,
                        name = "Agent Tool Resolution",
                        description = "LLM receives the prompt template, binds parameters, and identifies tool calls: user_profile(userId='Olga Podolyako').",
                        exampleSnippet = "tools/call: { name: 'user_profile', arguments: { userId: 'olga@contoso.com' } }"
                    },
                    new
                    {
                        stage = 4,
                        name = "Underlying OData / Graph API Execution",
                        description = "The tool executes the OData request described in the annotation examples against Microsoft Graph v1.0.",
                        exampleSnippet = "GET https://graph.microsoft.com/v1.0/users/olga@contoso.com/manager"
                    }
                }
            }
        };
    }
}
