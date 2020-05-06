/*******************************************************************************
 * Aloxi Alex Adapter
 *   AWS Lambda Function
 *   part of Aloxi - https://github.com/ZoolWay/Aloxi
 *   MIT license
 * 
 * Requires these environment variables:
 * IOT_ENDPOINT: Set to your IoT Core endpoint, 
 *   e.g. XXXXXXXXXX-ats.iot.eu-west-1.amazonaws.com
 * IOT_TOPIC: Set to the same topic name as the Aloxi Bridge on your device.
 * 
 * Requires policies to connect, pub, sub and receive on your AWS IoT Core.
 * Function will be endpoint to your Alexa Smart Home Skill.
 * Lamdba timeout should match your connections, 3s or less.
 * Note: This hobby solution comes without guarantees or warranties. Aloxi or
 * its author is not part of Amazon or AWS. Amazon, AWS and AWS product names
 * are trademarks of their respective owners.
 * ****************************************************************************/

const awsIot = require('aws-iot-device-sdk');
const util = require('util');
const decoder = new util.TextDecoder('utf-8');

const device = awsIot.jobs({ host: process.env.IOT_ENDPOINT, protocol: 'wss' });

device.on('connect', function () {
    device.subscribe(process.env.IOT_TOPIC);
});

let subscriber = null;
let lastEchoRequesst = null;

device.on('message', function (topic, encPayload) {
    if (encPayload == undefined) return;
    const strPayload = decoder.decode(encPayload);
    console.debug('incoming on topic \'' + topic + '\': ' + strPayload);
    const payload = JSON.parse(strPayload);

    if ((payload.type === undefined) || (payload.type !== 'aloxiComm')) return;
    if (payload.operation === undefined) return;
    console.debug('operation: ' + payload.operation);

    if (payload.operation === 'echoResponse') {
        if (subscriber) {
            subscriber(topic, payload.data);
        } else {
            throw new Error('Subscriber missing to handle received message');
        }
    } else {
        console.log("not relevant operation: " + payload.operation);
    }
});

async function performEchoRequest() {
    lastEchoRequesst = "from-lambda" + new Date().toISOString();
    const deviceRequest = { "type": "aloxiComm", "operation": "echo", "data": lastEchoRequesst };
    const promise = new Promise(function (resolve, reject) {
        subscriber = function (topic, payload) {
            console.log('received echo response, checking content');
            let message;
            if (payload == lastEchoRequesst) {
                message = 'echo matches';
            } else {
                message = 'echo response does not match request';
                console.error(message);
            }
            resolve({ 'message': message });
        };
    });

    await device.publish(process.env.IOT_TOPIC, JSON.stringify(deviceRequest, null, ''), { qos: 0 }, function (err, data) {
        //console.log("publishing message to device", err, data);
    });

    return promise;
}

exports.handler = async function (req, context) {
    if (!(req)) throw new Error('Request missing');
    if (!(req.header)) throw new Error('Request header missing');
    if (!(req.header.name)) throw new Error('Request name missing');

    const reqName = req.header.name;
    let responseObject = null;
    let responsePromise = null;
    console.debug(`Handling request named '${reqName}'`);

    switch (reqName) {
        case 'AloxiEchoRequest':
            responsePromise = await performEchoRequest();
            break;

        case 'DiscoverAppliancesRequest':
        case 'TurnOnRequest':
        case 'TurnOffRequest':
        case 'SetPercentageRequest':
        case 'IncrementPercentageRequest':
        case 'DecrementPercentageRequest':
        default:
            console.error(`Unsupported request '${reqName}', ignoring`);
            responseObject = {};
            break;
    }

    if (responsePromise !== null) {
        console.debug('Returning promise');
        return responsePromise;
    }
    if (responseObject !== null) {
        console.debug('Returning object');
        return responseObject;
    }
    throw new Error('Response computing failed');
};
