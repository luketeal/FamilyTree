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

export async function getMany(store, ids) {
    const db = await open();
    const os = tx(db, [store], 'readonly').objectStore(store);
    const results = await Promise.all(ids.map(id => request(os.get(id))));
    return results.filter(r => r !== undefined);
}

export async function put(store, record) {
    const db = await open();
    const transaction = tx(db, [store], 'readwrite');
    transaction.objectStore(store).put(record);
    await done(transaction);
}

/** One transaction for many records, so a multi-record change cannot half-apply. */
export async function putMany(store, records) {
    const db = await open();
    const transaction = tx(db, [store], 'readwrite');
    const os = transaction.objectStore(store);
    for (const record of records) os.put(record);
    await done(transaction);
}

export async function remove(store, id) {
    const db = await open();
    const transaction = tx(db, [store], 'readwrite');
    transaction.objectStore(store).delete(id);
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

export async function clearAll() {
    const db = await open();
    const transaction = tx(db, STORES, 'readwrite');
    for (const store of STORES) transaction.objectStore(store).clear();
    await done(transaction);
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
