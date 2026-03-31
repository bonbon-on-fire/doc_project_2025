const message = `<|instruction_start|>
{
  "id_message": "test-weather",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "get_weather", 
          "args": {
            "location": "San Francisco",
            "units": "celsius"
          }
        }
      ]
    }
  ]
}
<|instruction_end|>
Get the weather for San Francisco`.trim();

console.log('=== MESSAGE FORMAT BEING SENT ===');
console.log(JSON.stringify({userId: 'user-123', message: message}, null, 2));

console.log('\n=== INSTRUCTION PARSING TEST ===');
const startTag = '<|instruction_start|>';
const endTag = '<|instruction_end|>';
const start = message.indexOf(startTag);
const end = message.indexOf(endTag);

console.log('Start index:', start);
console.log('End index:', end);

if (start >= 0 && end > start) {
    const jsonSpan = message.substring(start + startTag.length, end).trim();
    console.log('Extracted JSON:');
    console.log(jsonSpan);
    
    try {
        const parsed = JSON.parse(jsonSpan);
        console.log('Successfully parsed:', JSON.stringify(parsed, null, 2));
        
        // Check tool_call structure
        if (parsed.messages && parsed.messages[0] && parsed.messages[0].tool_call) {
            console.log('✅ Tool call structure found');
            console.log('Tool calls:', JSON.stringify(parsed.messages[0].tool_call, null, 2));
        } else {
            console.log('❌ No tool_call structure found');
        }
    } catch (e) {
        console.log('❌ JSON parsing failed:', e.message);
    }
} else {
    console.log('❌ Instruction tags not found correctly');
}