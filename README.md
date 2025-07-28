# API Ekialis-SharePoint Synchronization

## 🎯 Objectif de l'API

Cette API permet de **synchroniser automatiquement** les listes de logiciels entre deux systèmes :
- **SharePoint** : La liste d'inventaire des logiciels (source de vérité)
- **Ekialis** : L'outil de cartographie IT

L'API maintient **SharePoint comme référence principale** et s'assure qu'Ekialis reflète toujours l'état de SharePoint.

## 🚀 Ce que fait cette API

### ✅ Synchronisation Automatique
- **Exécute toutes les heures** sans intervention
- Ajoute automatiquement les nouveaux logiciels de SharePoint vers Ekialis
- Met à jour les caractéristiques (domaine, référent, éditeur, etc.)
- Marque en rouge les logiciels supprimés de SharePoint

### ✅ Import de Fichiers Excel
- Importez les fichiers Excel directement dans SharePoint
- Mapping automatique des colonnes vers les bons champs
- Création en masse de logiciels

### ✅ Opérations Manuelles
- Synchronisation immédiate à la demande
- Contrôle précis sur chaque étape
- Monitoring en temps réel

## 🔧 Fonctionnalités Disponibles

### 🔄 Synchronisation Complète
```
POST /api/Synchronization/sharepoint-vers-ekialis-complet
```
**Rôle :** Lance une synchronisation complète en 3 étapes
1. Ajoute les logiciels manquants dans Ekialis
2. Met à jour toutes les caractéristiques 
3. Marque les logiciels obsolètes en rouge

**Quand l'utiliser :** Pour une synchronisation complète immédiate

---

### 📊 Import Excel
```
POST /api/Operations/importer-excel-vers-sharepoint
```
**Rôle :** Importe un fichier Excel dans SharePoint
- Lit le fichier Excel ligne par ligne
- Crée automatiquement les logiciels dans SharePoint
- Mappe les colonnes Excel vers les bons champs SharePoint

**Quand l'utiliser :** Quand il y a une liste Excel à intégrer

**Format Excel attendu :**
| Colonne Excel | Description |
|---------------|-------------|
| APPLICATION | Nom du logiciel (obligatoire) |
| FOURNISSEUR | Éditeur/Fournisseur |
| SERVICE/ENTITE | Service utilisateur |
| ROLE | Fonction du logiciel |
| PRIX | Coût de la licence |
| Référent NGE | Responsable interne |
| Contact Commercial - Nom, Prénom | Contact fournisseur |
| Contact Commercial - Téléphone | Téléphone contact |
| Contact Commercial - Mail | Email contact |
| LIEN EDITEUR | Site web éditeur |
| Pérénité Solution | Statut de maintenance |

---

### ⚙️ Opérations Unitaires

#### Ajouter SharePoint → Ekialis
```
POST /api/Operations/ajouter-sharepoint-vers-ekialis
```
**Rôle :** Ajoute uniquement les logiciels manquants dans Ekialis
**Quand l'utiliser :** Après avoir ajouté des logiciels dans SharePoint

#### Mettre à jour les caractéristiques
```
POST /api/Operations/mettre-a-jour-caracteristiques
```
**Rôle :** Synchronise les informations (domaine, référent, éditeur) depuis SharePoint
**Quand l'utiliser :** Après avoir modifié des informations dans SharePoint

#### Marquer les obsolètes
```
POST /api/Operations/marquer-obsoletes-rouge
```
**Rôle :** Marque en rouge les logiciels supprimés de SharePoint
**Quand l'utiliser :** Pour identifier visuellement les logiciels à supprimer

---

### 🔍 Monitoring et Tests

#### Test d'authentification
```
GET /api/Synchronization/test-authentification
```
**Rôle :** Vérifie que les identifiants fonctionnent

#### Statut de la synchronisation
```
GET /api/Synchronization/statut-synchronisation-automatique
```
**Rôle :** Affiche l'état de la synchronisation automatique

## 🔒 Sécurité

L'API est protégée par **authentification Basic Auth** :
- Username : `qjoly@nge.fr`
- Password : `quentinEkialis`

**Tous les appels nécessitent cette authentification.**

## 💡 Cas d'Usage Typiques

### 📋 Nouveau projet avec fichier Excel
1. `POST /api/Operations/importer-excel-vers-sharepoint` - Importer le fichier Excel
2. `POST /api/Synchronization/sharepoint-vers-ekialis-complet` - Synchroniser tout

### 🔄 Maintenance quotidienne
- La synchronisation automatique s'en charge (toutes les heures)
- Aucune action requise

### ➕ Ajout ponctuel de logiciels
1. Ajoutez le logiciel dans SharePoint manuellement
2. `POST /api/Operations/ajouter-sharepoint-vers-ekialis` - Le pousser vers Ekialis

### 📝 Mise à jour d'informations
1. Modifiez les informations dans SharePoint
2. `POST /api/Operations/mettre-a-jour-caracteristiques` - Synchroniser les changements

### 🗑️ Suppression de logiciels
1. Supprimez le logiciel de SharePoint
2. `POST /api/Operations/marquer-obsoletes-rouge` - Le marquer comme obsolète dans Ekialis

## 🎯 Flux de Données

```
SharePoint (Source de vérité)
       ↓
   📊 Excel Import
       ↓
🔄 Synchronisation Auto (1h)
       ↓
   Ekialis (Miroir)
       ↓
🔴 Logiciels obsolètes marqués en rouge
```

## 🚀 Installation Rapide

### Configuration
1. Modifiez `appsettings.json` avec les paramètres SharePoint et Ekialis
2. Lancez l'application : `dotnet run`
3. L'API sera disponible sur `https://localhost:5001`

### Premier Test
```bash
# Tester l'authentification
curl -u "qjoly@nge.fr:quentinEkialis" \
  https://localhost:5001/api/Synchronization/test-authentification

# Lancer une synchronisation complète
curl -X POST -u "qjoly@nge.fr:quentinEkialis" \
  https://localhost:5001/api/Synchronization/sharepoint-vers-ekialis-complet
```

### 📊 Interface Swagger

Accédez à `https://localhost:5001` pour une interface graphique complète avec :
- Documentation interactive de tous les endpoints
- Possibilité de tester directement depuis le navigateur
- Exemples de réponses pour chaque appel

## ⚡ Avantages de cette API

✅ **Automatisation complète** - Plus besoin de synchroniser manuellement  
✅ **Source de vérité unique** - SharePoint reste la référence  
✅ **Import Excel facile** - Intégration des fichiers existants  
✅ **Contrôle granulaire** - Choix de ce qui est synchronisé et quand  
✅ **Sécurité** - Authentification sur tous les endpoints  
✅ **Monitoring** - Logs détaillés de toutes les opérations  
✅ **Fiabilité** - Gestion d'erreurs et retry automatique  

## 🎯 Résultat Final

Avec cette API, on obtient :
- **SharePoint** : L'inventaire complet et à jour
- **Ekialis** : Cartographie automatiquement synchronisée
- **Logiciels obsolètes** : Clairement identifiés en rouge
- **Nouvelles additions** : Automatiquement propagées
- **Zéro maintenance** : Synchronisation automatique toutes les heures

**L'objectif est simple : Maintenir SharePoint, l'API s'occupe d'Ekialis !** 🎯

---

**Version :** 2.0.0  
**Support :** qjoly@nge.fr  
**Documentation Swagger :** https://localhost:5001
