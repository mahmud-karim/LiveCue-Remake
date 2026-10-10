import Foundation

/// Catalogue decoding, filter and sort. Internal; reached by tests with `@testable import LiveCueCore`.
enum OpenRouterCatalogue {
    /// A malformed entry is skipped (CORE-A5); only a missing or non-array `data` fails. Sorted by
    /// lower-cased name, then by id, without locale rules (CORE-B4).
    ///
    /// Spec 02 section 4.3: an entry is kept when it takes text in, gives text out and is not a
    /// `:batch` variant. Every option has the one effort "default".
    static func options(from body: Data) throws -> [AssistantModelOption] {
        guard let entries = JSONValue.parse(body)?["data"]?.arrayValue else {
            throw OpenRouterError.invalidResponse
        }

        var options: [AssistantModelOption] = []
        for entry in entries {
            if let option = makeOption(from: entry) {
                options.append(option)
            }
        }

        // `lowercased()` and `<` on strings follow Unicode alone, never the device's language.
        return options.sorted { first, second in
            let firstName = first.name.lowercased()
            let secondName = second.name.lowercased()
            if firstName != secondName {
                return firstName < secondName
            }
            return first.id < second.id
        }
    }

    /// nil for an entry that is malformed, and for one the filter leaves out.
    private static func makeOption(from entry: JSONValue) -> AssistantModelOption? {
        guard let id = entry["id"]?.stringValue,
              let name = entry["name"]?.stringValue,
              let input = entry["architecture"]?["input_modalities"]?.arrayValue,
              let output = entry["architecture"]?["output_modalities"]?.arrayValue
        else {
            return nil
        }

        let text = JSONValue.string("text")
        guard input.contains(text), output.contains(text), !id.contains(":batch") else {
            return nil
        }

        let parameters = entry["supported_parameters"]?.arrayValue ?? []
        let pricing = entry["pricing"]
        return AssistantModelOption(
            id: id,
            name: name,
            reasoningEfforts: ["default"],
            // Always written, true or false: never absent for an OpenRouter option.
            structuredOutputs: parameters.contains(JSONValue.string("structured_outputs")),
            // A price that is not a string counts as absent.
            promptPrice: pricing?["prompt"]?.stringValue,
            completionPrice: pricing?["completion"]?.stringValue
        )
    }
}
