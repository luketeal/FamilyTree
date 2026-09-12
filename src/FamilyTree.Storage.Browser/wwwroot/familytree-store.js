// IndexedDB access for the repository implementations.
//
// Stores are keyed by record id and read in bulk, mirroring how a future HTTP
// backend would be addressed: getAll for a collection, getMany for a batch,
// never a loop of single reads from the caller.

const DB_NAME = 'familytree';
const DB_VERSION = 1;

export const STORES = ['people', 'biologicalLinks', 'adoptiveLinks', 'marriages', 'stepparentLinks', 'meta'];

let dbPromise = null;

function open() {
    if (dbPromise) return dbPromise;

    dbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, DB_VERSION);

        request.onupgradeneeded = () => {
            const db = request.result;
            for (const store of STORES) {
                if (!db.objectStoreNames.contains(store)) {
                    db.createObjectStore(store, { keyPath: 'id' });
                }
            }
        };

        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        // Another tab holding an older version open would block the upgrade
        // indefinitely; failing loudly beats hanging with no explanation.
        request.onblocked = () => reject(new Error('IndexedDB upgrade blocked by another open tab.'));
    });

    // Without this a single failure is cached for the life of the page: every
    // later call returns the same rejected promise. The blocked case is exactly
    // the one that recovers on its own once the other tab closes, so a retry has
    // to be possible. Attached to a branch of the chain rather than reassigned
    // into it, so callers still see the rejection.
    dbPromise.catch(() => { dbPromise = null; });

    return dbPromise;
}

function tx(db, stores, mode) {
    return db.transaction(stores, mode);
}

function done(transaction) {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error ?? new Error('Transaction aborted.'));
    });
}

function request(req) {
    return new Promise((resolve, reject) => {
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

export async function getAll(store) {
    const db = await open();
    return await request(tx(db, [store], 'readonly').objectStore(store).getAll());
}

export async function get(store, id) {
    const db = await open();
    return (await request(tx(db, [store], 'readonly').objectStore(store).get(id))) ?? null;
}

export async function put(store, record) {
    const db = await open();
    const transaction = tx(db, [store], 'readwrite');
    transaction.objectStore(store).put(record);
    await done(transaction);
}

export async function remove(store, id) {
    const db = await open();
    const transaction = tx(db, [store], 'readwrite');
    transaction.objectStore(store).delete(id);
    await done(transaction);
}

/**
 * Several records from one store by key, in a single transaction.
 *
 * Keys that match nothing are simply absent from the result — a relationship
 * pointing at a deleted person is a state the app can reach, and it is the
 * caller's business to decide what to do about it.
 */
export async function getMany(store, ids) {
    const db = await open();
    const os = tx(db, [store], 'readonly').objectStore(store);
    const found = await Promise.all(ids.map(id => request(os.get(id))));
    return found.filter(record => record !== undefined && record !== null);
}

/**
 * Deletes one record and writes another in the SAME transaction.
 *
 * Correcting a wrongly recorded parent touches two rows. As two calls it can
 * half-apply — the delete commits, the put fails, and the child is left with a
 * parent slot that silently emptied itself. IndexedDB gives atomicity for free
 * within a transaction, so the operation is expressed as one.
 */
export async function replaceOne(store, deleteId, record) {
    const db = await open();
    const transaction = tx(db, [store], 'readwrite');
    const os = transaction.objectStore(store);
    os.delete(deleteId);
    os.put(record);
    await done(transaction);
}

/** Replaces the entire dataset atomically — used by import and sample data. */
export async function replaceAll(payload) {
    const db = await open();
    const transaction = tx(db, STORES, 'readwrite');
    for (const store of STORES) {
        const os = transaction.objectStore(store);
        os.clear();
        for (const record of payload[store] ?? []) os.put(record);
    }
    await done(transaction);
}

/**
 * Empties the data stores and re-stamps the schema version. Clearing the stamp
 * along with the data would leave an unstamped database that later writes go
 * into unlabelled, so the version is rewritten in the same transaction.
 */
export async function clearAll(meta) {
    const db = await open();
    const transaction = tx(db, STORES, 'readwrite');
    for (const store of STORES) transaction.objectStore(store).clear();
    transaction.objectStore('meta').put(meta);
    await done(transaction);
}

/**
 * Browser-local settings that are not part of the tree — currently only the
 * timestamp of the last export.
 *
 * localStorage rather than an IndexedDB store, because these are facts about
 * this device rather than about the family: replacing the whole dataset (a
 * sample load, a restore) must not also rewrite when this browser last made a
 * backup. Both calls swallow their errors: Safari in private mode throws on
 * write, and losing the backup reminder must never cost the user the export.
 */
export function readSetting(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function writeSetting(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        // Storage unavailable or full. The reminder degrades to "never
        // exported"; the export itself already happened.
    }
}

/**
 * Asks the browser to exempt this origin from routine eviction. Without it the
 * tree is "best effort" storage and can be discarded under pressure — which for
 * a genealogy record is data loss, not a cache miss.
 */
export async function requestPersistence() {
    if (!navigator.storage?.persist) return { supported: false, persisted: false, usageBytes: null };

    const persisted = (await navigator.storage.persisted()) || (await navigator.storage.persist());
    let usageBytes = null;
    if (navigator.storage.estimate) {
        usageBytes = (await navigator.storage.estimate()).usage ?? null;
    }

    return { supported: true, persisted, usageBytes };
}
