// Bridge to the SDK implementation
import { createEvent as sdkCreateEvent, getOutbox as sdkGetOutbox, syncOutbox as sdkSyncOutbox } from '../../WorkTime.ClientSdk/pwa-client.js';

export async function createEvent(params){
  return sdkCreateEvent(params);
}

export async function getOutbox(){
  return sdkGetOutbox();
}

export async function syncOutbox(opts){
  return sdkSyncOutbox(opts);
}
