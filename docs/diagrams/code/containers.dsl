workspace "Let's Chat" "E2EE encrypted chat PWA with built-in translation" {

    !identifiers hierarchical

    model {
        user = person "User" "Desktop web user (primary) or Android PWA install."

        letsChat = softwareSystem "Let's Chat" "E2EE chat PWA with opt-in translation." {

            pwa = container "Web App (PWA)" {
                technology "React + TypeScript, Vite"
                description "Holds private keys; all encryption/decryption happens here."
            }

            vault = container "Local Vault" {
                technology "IndexedDB + WebCrypto"
                description "Message history, contacts, cached translations — encrypted at rest."
                tags "Database"
            }

            api = container "API & Relay" {
                technology "ASP.NET Core (.NET), raw WebSocket"
                description "Relays ciphertext; proxies consented translations."
            }

            db = container "Database" {
                technology "PostgreSQL (Docker)"
                description "Users, prekeys, ciphertext envelopes (TTL)."
            }
        }

        deepl = softwareSystem "DeepL API" "External NMT translation provider."

        user -> letsChat.pwa "Chats, reads, translates"
        letsChat.pwa -> letsChat.vault "Stores decrypted history (encrypted at rest)"
        letsChat.pwa -> letsChat.api "Envelopes, prekey fetch, contacts" "WSS"
        letsChat.api -> letsChat.pwa "Envelope delivery" "WSS"
        letsChat.pwa -> letsChat.api "Translation requests" "HTTPS"
        letsChat.api -> letsChat.db "Reads/writes" "SQL"
        letsChat.api -> deepl "Translation text" "HTTPS"
    }

    views {
        container letsChat "Containers" {
            include *
            autoLayout
            description "Let's Chat containers."
        }

        dynamic letsChat "MessageFlow" "A (zh) sends a message; B (es) receives and translates." {
            user -> letsChat.pwa "1. Types message, taps send"
            letsChat.pwa -> letsChat.api "2. Fetches B's prekey bundle; sends encrypted envelope" "WSS"
            letsChat.api -> letsChat.db "3. Stores envelope if B offline (TTL)"
            letsChat.api -> letsChat.pwa "4. Delivers envelope" "WSS"
            letsChat.pwa -> letsChat.vault "5. Decrypts locally; stores in encrypted history"
            user -> letsChat.pwa "6. Taps Translate (consent)"
            letsChat.pwa -> letsChat.api "7. Sends plaintext for translation" "HTTPS"
            letsChat.api -> deepl "8. Forwards text (in-memory only)" "HTTPS"
            letsChat.api -> letsChat.pwa "9. Returns translation"
            letsChat.pwa -> letsChat.vault "10. Caches translation in vault"
            autoLayout
        }

        styles {
            element "Person" {
                shape Person
            }
            element "Software System" {
                background #1168bd
                color #ffffff
            }
            element "Container" {
                background #438dd5
                color #ffffff
            }
            element "Database" {
                shape Cylinder
            }
        }
    }
}
