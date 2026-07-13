#!/bin/bash
# Bash script to test the new search algorithm
# Usage: ./test-search-algorithm.sh

API_URL="http://localhost:5000/api/search"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
GRAY='\033[0;90m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}Testing Multi-Stage Search Algorithm${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""

# Check if API is running
if ! curl -s -X POST "$API_URL" -H "Content-Type: application/json" -d '{"query":"test"}' > /dev/null 2>&1; then
    echo -e "${RED}ERROR: API is not running at $API_URL${NC}"
    echo -e "${YELLOW}Please start the API first:${NC}"
    echo -e "${YELLOW}  cd FatawaAI.Api${NC}"
    echo -e "${YELLOW}  dotnet run${NC}"
    exit 1
fi

# Test cases
declare -a queries=(
    "حكم شراء باستخدام تابي وتمارا|installment purchases, Tabby/Tamara"
    "حكم صلاة الجمعة في المنزل|Friday prayer at home"
    "حكم البيع بالأجل|deferred payment sales"
    "حكم القرض من البنك|bank loans, riba"
    "حكم استخدام الذكاء الاصطناعي في الفتاوى|empty results (topic not in corpus)"
)

test_num=1
for query_info in "${queries[@]}"; do
    IFS='|' read -r query expected <<< "$query_info"
    
    echo -e "${YELLOW}----------------------------------------${NC}"
    echo -e "${YELLOW}Test $test_num: $expected${NC}"
    echo -e "${WHITE}Query: $query${NC}"
    echo ""

    start_time=$(date +%s.%N)
    response=$(curl -s -X POST "$API_URL" \
        -H "Content-Type: application/json" \
        -d "{\"query\":\"$query\"}")
    end_time=$(date +%s.%N)
    
    duration=$(echo "$end_time - $start_time" | bc)
    
    echo -e "${GREEN}✓ Response received in ${duration}s${NC}"
    echo ""

    # Parse response
    result_count=$(echo "$response" | jq -r '.results | length')
    
    if [ "$result_count" -eq 0 ]; then
        echo -e "${MAGENTA}Results: EMPTY (0 fatwas)${NC}"
        disclaimer=$(echo "$response" | jq -r '.disclaimer')
        echo -e "${GRAY}Disclaimer: $disclaimer${NC}"
    else
        echo -e "${GREEN}Results: $result_count fatwas found${NC}"
        echo ""
        
        # Show first 3 results
        for i in {0..2}; do
            if [ $i -lt $result_count ]; then
                fatwa_id=$(echo "$response" | jq -r ".results[$i].fatwaId")
                title=$(echo "$response" | jq -r ".results[$i].title")
                question=$(echo "$response" | jq -r ".results[$i].question" | cut -c1-100)
                
                echo -e "${CYAN}  [$((i+1))] Fatwa #$fatwa_id${NC}"
                echo -e "${WHITE}      Title: $title${NC}"
                echo -e "${GRAY}      Question: $question...${NC}"
                echo ""
            fi
        done
        
        if [ $result_count -gt 3 ]; then
            echo -e "${GRAY}  ... and $((result_count - 3)) more results${NC}"
            echo ""
        fi
    fi

    # Show analysis if available
    fiqh_topic=$(echo "$response" | jq -r '.analysis.fiqhTopic // empty')
    if [ -n "$fiqh_topic" ]; then
        echo -e "${CYAN}Analysis:${NC}"
        echo -e "${WHITE}  Fiqh Topic: $fiqh_topic${NC}"
        keywords=$(echo "$response" | jq -r '.analysis.keywords | join(", ")')
        echo -e "${WHITE}  Keywords: $keywords${NC}"
        echo ""
    fi

    test_num=$((test_num + 1))
    sleep 1
done

echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}Testing Complete${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "${YELLOW}Review the results above to verify:${NC}"
echo -e "${WHITE}1. Queries return topically relevant fatwas${NC}"
echo -e "${WHITE}2. Response times are 2-5 seconds${NC}"
echo -e "${WHITE}3. Empty results for queries with no matches${NC}"
echo -e "${WHITE}4. Analysis extracts correct fiqh topics${NC}"
echo ""
echo -e "${YELLOW}Check the API console logs for:${NC}"
echo -e "${WHITE}- Semantic filter retention rates${NC}"
echo -e "${WHITE}- Confidence levels (HIGH/LOW)${NC}"
echo -e "${WHITE}- Any error messages${NC}"

