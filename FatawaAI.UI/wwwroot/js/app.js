// API Configuration - Points to the separate API project
const API_BASE_URL = 'http://localhost:5123/api/search';

// DOM Elements
const searchInput = document.getElementById('searchInput');
const searchButton = document.getElementById('searchButton');
const loadingState = document.getElementById('loadingState');
const resultsSection = document.getElementById('resultsSection');
const emptyState = document.getElementById('emptyState');
const errorState = document.getElementById('errorState');
const queryInfo = document.getElementById('queryInfo');
const analysisCard = document.getElementById('analysisCard');
const resultsContainer = document.getElementById('resultsContainer');
const retryButton = document.getElementById('retryButton');

// Example buttons
const exampleButtons = document.querySelectorAll('.example-btn');

// State
let currentQuery = '';
let isSearching = false;

// Event Listeners
searchButton.addEventListener('click', handleSearch);
searchInput.addEventListener('keypress', (e) => {
    if (e.key === 'Enter' && !isSearching) {
        handleSearch();
    }
});

exampleButtons.forEach(btn => {
    btn.addEventListener('click', () => {
        searchInput.value = btn.dataset.query;
        handleSearch();
    });
});

retryButton.addEventListener('click', () => {
    if (currentQuery) {
        performSearch(currentQuery);
    }
});

// Main search handler
async function handleSearch() {
    const query = searchInput.value.trim();
    
    if (!query) {
        showError('الرجاء إدخال سؤال للبحث');
        return;
    }
    
    if (query.length < 3) {
        showError('الرجاء إدخال سؤال أطول (3 أحرف على الأقل)');
        return;
    }
    
    if (query.length > 500) {
        showError('السؤال طويل جداً، الرجاء اختصاره');
        return;
    }
    
    currentQuery = query;
    await performSearch(query);
}

// Perform search API call
async function performSearch(query) {
    if (isSearching) return;
    
    isSearching = true;
    hideAllStates();
    showLoading();
    disableSearch();
    
    const startTime = Date.now();
    
    try {
        const response = await fetch(API_BASE_URL, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({ query })
        });
        
        const duration = ((Date.now() - startTime) / 1000).toFixed(2);
        
        if (!response.ok) {
            throw new Error(`خطأ في الخادم: ${response.status}`);
        }
        
        const data = await response.json();
        displayResults(data, duration);
        
    } catch (error) {
        console.error('Search error:', error);
        showError(`حدث خطأ أثناء البحث: ${error.message}`);
    } finally {
        isSearching = false;
        enableSearch();
    }
}

// Display search results
function displayResults(data, duration) {
    hideAllStates();
    
    // Show query info
    queryInfo.innerHTML = `
        <strong>البحث عن:</strong> "${data.query}" 
        <span style="color: var(--text-muted); margin-right: 1rem;">
            (${duration} ثانية | ${data.results.length} نتيجة)
        </span>
    `;
    
    // Show analysis if available
    if (data.analysis && data.analysis.fiqhTopic) {
        document.getElementById('fiqhTopic').textContent = data.analysis.fiqhTopic || 'غير محدد';
        document.getElementById('keywords').textContent = 
            data.analysis.keywords && data.analysis.keywords.length > 0 
                ? data.analysis.keywords.join(' • ') 
                : 'لا توجد';
        analysisCard.classList.remove('hidden');
    } else {
        analysisCard.classList.add('hidden');
    }
    
    // Show results or empty state
    if (data.results && data.results.length > 0) {
        displayFatwas(data.results);
        resultsSection.classList.remove('hidden');
    } else {
        showEmpty(data.disclaimer || 'لم يتم العثور على فتاوى مطابقة لسؤالك');
    }
    
    // Show attribution
    document.getElementById('sourceAttribution').textContent = data.sourceAttribution;
    document.getElementById('disclaimer').textContent = data.disclaimer;
}

// Display fatwa cards
function displayFatwas(fatwas) {
    resultsContainer.innerHTML = '';
    
    fatwas.forEach((fatwa, index) => {
        const card = createFatwaCard(fatwa, index + 1);
        resultsContainer.appendChild(card);
    });
}

// Create individual fatwa card
function createFatwaCard(fatwa, number) {
    const card = document.createElement('div');
    card.className = 'fatwa-card';
    
    // Create categories badges
    const categoriesHTML = fatwa.categories && fatwa.categories.length > 0
        ? `<div class="fatwa-categories">
            ${fatwa.categories.map(cat => 
                `<span class="category-badge">${cat}</span>`
            ).join('')}
           </div>`
        : '';
    
    // Truncate long text
    const truncateText = (text, maxLength = 300) => {
        if (!text) return '';
        return text.length > maxLength 
            ? text.substring(0, maxLength) + '...' 
            : text;
    };
    
    card.innerHTML = `
        <div class="fatwa-header">
            <div class="fatwa-number">#${number}</div>
            <div class="fatwa-collection">${fatwa.collectionType}</div>
        </div>
        
        <h3 class="fatwa-title">${escapeHtml(fatwa.title)}</h3>
        
        <div class="fatwa-section">
            <div class="fatwa-section-title">السؤال</div>
            <div class="fatwa-text">${escapeHtml(truncateText(fatwa.question, 400))}</div>
        </div>
        
        <div class="fatwa-section">
            <div class="fatwa-section-title">الجواب</div>
            <div class="fatwa-text fatwa-answer">
                ${escapeHtml(truncateText(fatwa.answer, 600))}
            </div>
        </div>
        
        ${categoriesHTML}
        
        <div class="fatwa-footer">
            <a href="${fatwa.sourceUrl}" target="_blank" rel="noopener" class="fatwa-link">
                <svg class="link-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"/>
                </svg>
                <span>قراءة الفتوى كاملة</span>
            </a>
            ${fatwa.audioUrl ? `
                <a href="${fatwa.audioUrl}" target="_blank" rel="noopener" class="fatwa-link fatwa-audio-link">
                    <svg class="link-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M9 18V5l12-2v13M9 18a3 3 0 100-6 3 3 0 000 6zm12-3a3 3 0 100-6 3 3 0 000 6z"/>
                    </svg>
                    <span>الاستماع</span>
                </a>
            ` : ''}
        </div>
    `;
    
    return card;
}

// Utility functions
function hideAllStates() {
    loadingState.classList.add('hidden');
    resultsSection.classList.add('hidden');
    emptyState.classList.add('hidden');
    errorState.classList.add('hidden');
}

function showLoading() {
    loadingState.classList.remove('hidden');
}

function showEmpty(message) {
    document.getElementById('emptyMessage').textContent = message;
    emptyState.classList.remove('hidden');
}

function showError(message) {
    document.getElementById('errorMessage').textContent = message;
    errorState.classList.remove('hidden');
}

function disableSearch() {
    searchButton.disabled = true;
    searchInput.disabled = true;
    searchButton.querySelector('.button-text').textContent = 'جاري البحث...';
}

function enableSearch() {
    searchButton.disabled = false;
    searchInput.disabled = false;
    searchButton.querySelector('.button-text').textContent = 'بحث';
}

function escapeHtml(unsafe) {
    if (!unsafe) return '';
    return unsafe
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

// Auto-focus search input on load
window.addEventListener('load', () => {
    searchInput.focus();
});

// Show welcome message on first load
if (!sessionStorage.getItem('hasSearched')) {
    console.log('Welcome to Fatawa.AI - محرك بحث الفتاوى');
}


