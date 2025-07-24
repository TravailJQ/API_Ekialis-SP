# API Ekialis-SharePoint Synchronization

Une API .NET Core sécurisée pour synchroniser les données entre Ekialis et SharePoint, avec import Excel et synchronisation automatique.

## 🚀 Fonctionnalités

### 🔒 Authentification
- **Authentification Basic Auth** : Sécurisation de tous les endpoints
- **Multi-utilisateurs** : Support de plusieurs comptes autorisés
- **Logs de sécurité** : Traçabilité des connexions

### 🔄 Synchronisation Complète
- **Synchronisation manuelle complète** : SharePoint → Ekialis (3 phases)
- **Synchronisation automatique** : Exécution toutes les heures
- **SharePoint comme source de vérité** : Les données SharePoint prévalent sur Ekialis

### ⚙️ Opérations Unitaires
- **SharePoint → Ekialis** : Ajoute les logiciels manquants dans Ekialis
- **Ekialis → SharePoint** : Ajoute les logiciels manquants dans SharePoint (manuel uniquement)
- **Synchronisation des caractéristiques** : Met à jour les valeurs selon SharePoint
- **Marquage des obsolètes** : Marque en rouge les logiciels supprimés de SharePoint

### 📊 Import Excel
- **Import de fichiers Excel** : Création automatique de logiciels dans SharePoint
- **Mapping automatique** : Correspondance des colonnes Excel vers les champs SharePoint
- **Validation des données** : Contrôle de la cohérence avant import

## 🏗️ Architecture

```
Controllers/
├── SynchronizationController.cs    # Synchronisations complètes + Auth test
└── OperationsController.cs         # Opérations unitaires + Import Excel

Services/
├── EkialisService.cs               # API Ekialis avec externalId unique
├── SharePointRestService.cs        # API SharePoint REST
└── SynchronizationBackgroundService.cs # Service automatique

Middleware/
└── BasicAuthMiddleware.cs          # Authentification Basic Auth

Models/
├── EkialisModels.cs                # Modèles Ekialis
└── FieldMapping.cs                 # Mapping des champs
```

## 🔧 Configuration

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "EkialisApi": {
    "BaseUrl": "https://nge.ekialis.com",
    "AuthEndpoint": "/api/auth",
    "Username": "qjoly@nge.fr",
    "Password": "quentinEkialis",
    "AuthKey": "fIR7EFSFlVkxZ5fq9NNGtYqfTiNauBiE"
  },
  "SharePoint": {
    "SiteUrl": "https://nge1.sharepoint.com/sites/SuiviDigitalDSI",
    "ListName": "INVENTAIRE LOGICIELS",
    "FedAuth": "cookie_fedauth_value",
    "RtFa": "cookie_rtfa_value",
    "Fields": "Title,field_1,field_2,field_3,field_6,field_9,field_13,field_14,field_15,field_25,field_27"
  }
}
```

### 🔐 Utilisateurs Autorisés

Par défaut, les utilisateurs suivants sont configurés :
- `qjoly@nge.fr` / `quentinEkialis`
- `admin` / `admin123`

**Pour ajouter d'autres utilisateurs**, modifiez `BasicAuthMiddleware.cs` :

```csharp
var validUsers = new Dictionary<string, string>
{
    { "qjoly@nge.fr", "quentinEkialis" },
    { "admin@nge.fr", "motdepasse123" },
    { "tech@nge.fr", "technique456" }
    // Ajoutez vos utilisateurs ici
};
```

### Mapping des Champs

| SharePoint | Ekialis | Description |
|------------|---------|-------------|
| Title | Nom du composant | Nom de l'application |
| field_1 | GEN-Domaine métier [Pôle NGE] | Service/Entité |
| field_2 | ACTEUR-Resp.Appli [Référent] | Référent NGE |
| field_3 | GEN-Description [Rôle] | Rôle de l'application |
| field_6 | GEN-Editeur | Fournisseur |
| field_9 | - | Prix (non mappé vers Ekialis) |
| field_13 | - | Contact Commercial - Nom |
| field_14 | - | Contact Commercial - Mail |
| field_15 | - | Contact Commercial - Téléphone |
| field_25 | - | Lien Éditeur |
| field_27 | - | Pérennité Solution |

## 📋 Endpoints

> ⚠️ **Tous les endpoints nécessitent une authentification Basic Auth**

### Test d'Authentification

#### `GET /api/Synchronization/auth-test`
Teste l'authentification Basic Auth.

**Exemple cURL :**
```bash
curl -X GET \
  https://votre-api/api/Synchronization/auth-test \
  -u "qjoly@nge.fr:quentinEkialis"
```

**Réponse :**
```json
{
  "message": "Authentification réussie",
  "timestamp": "2024-01-15T10:30:00",
  "user": "Accès autorisé"
}
```

### Synchronisation Complète

#### `POST /api/Synchronization/synchronisation-manuelle-complete`
Lance une synchronisation complète immédiate (3 phases).

**Exemple cURL :**
```bash
curl -X POST \
  https://votre-api/api/Synchronization/synchronisation-manuelle-complete \
  -u "qjoly@nge.fr:quentinEkialis"
```

**Réponse :**
```json
{
  "message": "Synchronisation manuelle complète terminée - SharePoint est la source de vérité",
  "timestamp": "2024-01-15T10:30:00",
  "phases": {
    "ajoutsVersEkialis": { "ajoutsReussis": 5, "ajoutsEchecs": 0 },
    "caracteristiques": { "caracteristiquesModifiees": 12, "caracteristiquesAjoutees": 3 },
    "marquageObsoletes": { "marquagesReussis": 2, "marquagesEchecs": 0 }
  }
}
```

#### `GET /api/Synchronization/status-synchronisation`
Statut de la synchronisation automatique.

### Opérations Unitaires

#### `POST /api/Operations/sharepoint-vers-ekialis`
Ajoute les logiciels SharePoint manquants dans Ekialis.

#### `POST /api/Operations/ekialis-vers-sharepoint`
Ajoute les logiciels Ekialis manquants dans SharePoint (manuel uniquement).

#### `POST /api/Operations/synchroniser-caracteristiques`
Met à jour les caractéristiques Ekialis selon SharePoint.

#### `POST /api/Operations/marquer-obsoletes-rouge`
Marque en rouge les logiciels obsolètes dans Ekialis.

### Import Excel

#### `POST /api/Operations/import-excel-vers-sharepoint`
Importe un fichier Excel vers SharePoint.

**Content-Type :** `multipart/form-data`  
**Paramètre :** `excelFile` (fichier Excel)

**Format Excel attendu :**

| Colonne Excel | Champ SharePoint | Description |
|---------------|------------------|-------------|
| APPLICATION | Title | Nom de l'application |
| FOURNISSEUR | field_6 | Nom du fournisseur |
| SERVICE/ENTITE | field_1 | Service ou entité |
| ROLE | field_3 | Rôle de l'application |
| PRIX | field_9 | Prix de la solution |
| Référent NGE | field_2 | Référent NGE |
| Contact Commercial - Nom, Prénom | field_13 | Contact commercial |
| Contact Commercial - Téléphone | field_15 | Téléphone du contact |
| Contact Commercial - Mail | field_14 | Email du contact |
| LIEN EDITEUR (Présentation Solution) | field_25 | Lien vers l'éditeur |
| Pérénité Solution | field_27 | Pérennité de la solution |

**Exemple cURL :**
```bash
curl -X POST \
  https://votre-api/api/Operations/import-excel-vers-sharepoint \
  -u "qjoly@nge.fr:quentinEkialis" \
  -F "excelFile=@mon_fichier.xlsx"
```

**Exemple Postman :**
1. Méthode : `POST`
2. URL : `https://votre-api/api/Operations/import-excel-vers-sharepoint`
3. Authorization : Basic Auth (`qjoly@nge.fr` / `quentinEkialis`)
4. Body : form-data avec clé `excelFile` (type File)

**Réponse :**
```json
{
  "message": "Import Excel vers SharePoint terminé",
  "fichier": "mon_fichier.xlsx",
  "totalLignes": 10,
  "ajoutsReussis": 9,
  "ajoutsEchecs": 1,
  "erreursDetaillees": ["Ligne ignorée: APPLICATION vide"]
}
```

## 🔄 Synchronisation Automatique

Le service automatique s'exécute **toutes les heures** et effectue :

1. **📥 Phase 1** : Ajout des logiciels SharePoint manquants dans Ekialis
2. **🔄 Phase 2** : Mise à jour des caractéristiques (SharePoint → Ekialis)
3. **🔴 Phase 3** : Marquage des logiciels obsolètes en rouge

### Logs typiques :
```
🚀 DÉBUT DE LA SYNCHRONISATION AUTOMATIQUE - 2024-01-15 10:00:00
📥 ÉTAPE 1: Ajout des logiciels SharePoint manquants dans Ekialis
JSON envoyé à Ekialis: {"name":"UMAP","externalId":"SP_UMAP_1704967200"}
✅ Logiciel 'UMAP' créé dans Ekialis avec ID: 12345 et externalId: SP_UMAP_1704967200
✅ Étape 1 terminée: 3 ajouts réussis, 0 échecs
🔄 ÉTAPE 2: Mise à jour des caractéristiques Ekialis selon SharePoint
✅ Étape 2 terminée: 5 modifications, 2 ajouts, 0 erreurs
🔴 ÉTAPE 3: Marquage des logiciels obsolètes en rouge
✅ Étape 3 terminée: 1 marquages réussis, 0 échecs
📊 SYNCHRONISATION COMPLÈTE TERMINÉE (SharePoint → Ekialis)
✅ FIN DE LA SYNCHRONISATION AUTOMATIQUE - 2024-01-15 10:05:23
⏳ EN ATTENTE - Prochaine synchronisation prévue à 2024-01-15 11:00:00
```

## 🚀 Installation et Démarrage

### Prérequis
- .NET Core 6.0+
- Accès à Ekialis avec clé API
- Accès à SharePoint avec cookies d'authentification
- DocumentFormat.OpenXml pour l'import Excel

### Installation
```bash
git clone https://github.com/votre-repo/api-ekialis-sharepoint
cd api-ekialis-sharepoint
dotnet restore
dotnet add package DocumentFormat.OpenXml --version 2.20.0
```

### Configuration
1. Modifiez `appsettings.json` avec vos paramètres
2. Configurez les cookies SharePoint (FedAuth, RtFa)
3. Vérifiez la clé API Ekialis
4. Ajoutez vos utilisateurs autorisés dans `BasicAuthMiddleware.cs`

### Démarrage
```bash
dotnet run
```

L'API sera disponible sur `https://localhost:5001`

## 🔐 Authentification

### Avec cURL
```bash
# Utilisation du flag -u
curl -X GET https://votre-api/api/Synchronization/auth-test \
  -u "qjoly@nge.fr:quentinEkialis"

# Ou avec l'header Authorization
curl -X GET https://votre-api/api/Synchronization/auth-test \
  -H "Authorization: Basic cWpvbHlAbmdlLmZyOnF1ZW50aW5Fa2lhbGlz"
```

### Avec Postman
1. Dans l'onglet "Authorization"
2. Type : "Basic Auth"
3. Username : `qjoly@nge.fr`
4. Password : `quentinEkialis`

### Avec JavaScript
```javascript
const username = 'qjoly@nge.fr';
const password = 'quentinEkialis';
const credentials = btoa(`${username}:${password}`);

fetch('/api/Synchronization/synchronisation-manuelle-complete', {
    method: 'POST',
    headers: {
        'Authorization': `Basic ${credentials}`,
        'Content-Type': 'application/json'
    }
});
```

### Erreurs d'authentification
```json
{
  "error": "Authentification requise",
  "status": 401,
  "message": "Veuillez fournir un username/password valide via Basic Auth"
}
```

## 📊 Monitoring et Logs

### Logs importants à surveiller :
- 🔒 **Authentification** : Connexions autorisées/refusées
- ❌ **Erreurs d'authentification** Ekialis/SharePoint
- 🔍 **Nombre de logiciels** synchronisés
- ⚠️ **Échecs d'ajout** ou de mise à jour
- 🔴 **Logiciels marqués** comme obsolètes
- 🆔 **ExternalId uniques** générés pour Ekialis

### Métriques clés :
- **Ajouts réussis/échecs** par synchronisation
- **Caractéristiques modifiées** par cycle
- **Logiciels obsolètes** détectés
- **Tentatives de connexion** non autorisées

### Exemples de logs :
```
✅ Connexion autorisée pour qjoly@nge.fr
❌ Tentative de connexion refusée pour unknown@user.com
✅ Logiciel 'UMAP' créé dans Ekialis avec ID: 12345 et externalId: SP_UMAP_1704967200
📝 Champ mappé: field_1 = 'IT Services'
✅ Champ direct: field_6 = Microsoft
```

## 🛠️ Développement

### Structure du code :
- **Controllers** : Endpoints REST avec authentification
- **Services** : Logique métier et APIs tierces  
- **Models** : Modèles de données et mappings
- **Middleware** : Authentification Basic Auth
- **Background Services** : Tâches automatiques

### Tests d'authentification :
```bash
# Test de connexion valide
curl -u "qjoly@nge.fr:quentinEkialis" \
  https://localhost:5001/api/Synchronization/auth-test

# Test de connexion invalide (doit retourner 401)
curl -u "wrong:credentials" \
  https://localhost:5001/api/Synchronization/auth-test
```

### Tests fonctionnels :
```bash
# Test synchronisation complète
curl -X POST -u "qjoly@nge.fr:quentinEkialis" \
  https://localhost:5001/api/Synchronization/synchronisation-manuelle-complete

# Test import Excel
curl -X POST -u "qjoly@nge.fr:quentinEkialis" \
  -F "excelFile=@test.xlsx" \
  https://localhost:5001/api/Operations/import-excel-vers-sharepoint
```

## 📝 Notes Importantes

- **🔒 Sécurité** : Tous les endpoints nécessitent une authentification Basic Auth
- **📊 SharePoint source de vérité** : Les données SharePoint prévalent toujours
- **🔄 Synchronisation unidirectionnelle** : SharePoint → Ekialis (sauf opération manuelle)
- **🔴 Logiciels obsolètes** : Marqués en rouge dans Ekialis si supprimés de SharePoint
- **📁 Import Excel** : Crée uniquement dans SharePoint, pas de synchronisation vers Ekialis immédiate
- **🆔 ExternalId unique** : Chaque logiciel créé dans Ekialis a un identifiant unique généré
- **⏰ Synchronisation automatique** : Toutes les heures, logs détaillés disponibles
- **🚫 Endpoints non protégés** : Seuls `/health` et `/status` sont accessibles sans authentification

## 🔧 Dépannage

### Problèmes courants :

**401 Unauthorized :**
- Vérifiez vos identifiants dans `BasicAuthMiddleware.cs`
- Utilisez le bon format : `username:password` en Base64

**Erreur ExternalId duplicate :**
- ✅ **Résolu** : L'API génère maintenant des externalId uniques automatiquement

**Erreur SharePoint cookies :**
- Renouvelez vos cookies FedAuth et RtFa
- Vérifiez l'URL du site SharePoint

**Erreur authentification Ekialis :**
- Vérifiez votre clé API dans appsettings.json
- Contrôlez l'URL de base Ekialis

---

**Dernière mise à jour :** Juillet 2025
**Version :** 2.0.0 (avec authentification Basic Auth)  
**Sécurité :** ✅ Authentification requise sur tous les endpoints
