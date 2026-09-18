workspace "Let's Chat" "E2EE encrypted chat PWA with built-in translation" {

    model {
        user = person "User" "Desktop web user (primary) or Android PWA install."

        letsChat = softwareSystem "Let's Chat" "E2EE chat PWA. Server relays only ciphertext; translation is opt-in."

        deepl = softwareSystem "DeepL API" "External NMT translation provider."

        user -> letsChat "Chats, reads, translates"
        letsChat -> deepl "Consented translation text" "HTTPS"
    }

    views {
        systemContext letsChat "SystemContext" {
            include *
            autoLayout
            description "System context for Let's Chat."
        }

        styles {
            element "Person" {
                shape Person
            }
            element "Software System" {
                background #1168bd
                color #ffffff
            }
        }
    }
}
